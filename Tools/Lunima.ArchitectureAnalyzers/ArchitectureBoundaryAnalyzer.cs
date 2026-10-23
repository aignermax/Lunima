using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Compiler-based architecture boundary verification over resolved dependencies.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArchitectureBoundaryAnalyzer : DiagnosticAnalyzer
{
    public const string ForbiddenDependencyId = "LUN001";
    public const string UnknownOwnershipId = "LUN002";
    public const string UncoveredProjectId = "LUN003";

    public static readonly DiagnosticDescriptor ForbiddenDependency = new(
        ForbiddenDependencyId, "Forbidden module dependency",
        "Module '{0}' cannot depend on '{1}' through '{2}'", "Architecture", DiagnosticSeverity.Error, true);
    public static readonly DiagnosticDescriptor UnknownOwnership = new(
        UnknownOwnershipId, "Unclassified architecture ownership",
        "Type '{0}' has no explicitly declared module", "Architecture", DiagnosticSeverity.Error, true);
    public static readonly DiagnosticDescriptor UncoveredProject = new(
        UncoveredProjectId, "Unclassified project",
        "Assembly '{0}' is missing from the architecture project manifest", "Architecture", DiagnosticSeverity.Error, true);

    private readonly ArchitecturePolicy _policy;

    public ArchitectureBoundaryAnalyzer() : this(ArchitecturePolicy.Repository) { }
    public ArchitectureBoundaryAnalyzer(ArchitecturePolicy policy) => _policy = policy;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(ForbiddenDependency, UnknownOwnership, UncoveredProject);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            if (!_policy.Assemblies.Contains(start.Compilation.AssemblyName ?? ""))
            {
                start.RegisterCompilationEndAction(end => end.ReportDiagnostic(Diagnostic.Create(
                    UncoveredProject, Location.None, end.Compilation.AssemblyName ?? "")));
                return;
            }
            var collector = new ArchitectureDependencyCollector(_policy);
            start.RegisterSymbolAction(symbol =>
            {
                var type = (INamedTypeSymbol)symbol.Symbol;
                var location = type.Locations.FirstOrDefault(l => l.IsInSource);
                if (location != null && !type.IsImplicitlyDeclared)
                    collector.CheckOwnership(type, location, symbol.ReportDiagnostic);
            }, SymbolKind.NamedType);
            start.RegisterSyntaxNodeAction(node =>
            {
                // An unused import has no semantic dependency; actual uses bind through its alias/global scope.
                if (node.Node.AncestorsAndSelf().Any(n => n is UsingDirectiveSyntax)) return;
                var enclosing = node.SemanticModel.GetEnclosingSymbol(node.Node.SpanStart, node.CancellationToken);
                var source = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
                if (source == null && node.Node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault() is { } declaration)
                    source = node.SemanticModel.GetDeclaredSymbol(declaration, node.CancellationToken) as INamedTypeSymbol;
                if (source == null) return;
                var target = node.SemanticModel.GetSymbolInfo(node.Node, node.CancellationToken).Symbol;
                collector.CheckReference(source, target, node.Node.GetLocation(), node.ReportDiagnostic);
            }, SyntaxKind.IdentifierName, SyntaxKind.GenericName, SyntaxKind.Attribute);
            start.RegisterOperationBlockAction(block =>
            {
                var source = block.OwningSymbol as INamedTypeSymbol ?? block.OwningSymbol.ContainingType;
                if (source == null) return;
                var walker = new ArchitectureOperationWalker(collector, source, block.ReportDiagnostic);
                foreach (var operation in block.OperationBlocks) walker.Visit(operation);
            });
        });
    }
}
