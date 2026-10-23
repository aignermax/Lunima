using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Resolves ownership and recursively inspects the types exposed by a referenced symbol.</summary>
internal sealed class ArchitectureDependencyCollector
{
    private readonly ArchitecturePolicy _policy;
    private readonly ConcurrentDictionary<string, byte> _reported = new();

    public ArchitectureDependencyCollector(ArchitecturePolicy policy) => _policy = policy;

    public void CheckOwnership(INamedTypeSymbol type, Location location, Action<Diagnostic> report)
    {
        if (Module(type) == null) Unknown(type, location, report);
    }

    public void CheckReference(INamedTypeSymbol source, ISymbol? dependency, Location location, Action<Diagnostic> report)
    {
        if (dependency is IAliasSymbol alias) dependency = alias.Target;
        if (dependency == null || dependency is INamespaceSymbol) return;
        var sourceModule = Module(source);
        if (sourceModule == null)
        {
            Unknown(source, source.Locations.FirstOrDefault(l => l.IsInSource) ?? location, report);
            return;
        }
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        void Type(ITypeSymbol? type) => CheckType(sourceModule, type, location, report, visited);
        if (dependency is ITypeSymbol direct) Type(direct);
        else
        {
            Type(dependency.ContainingType);
            switch (dependency)
            {
                case IMethodSymbol method:
                    Type(method.ReturnType);
                    foreach (var argument in method.TypeArguments) Type(argument);
                    foreach (var parameter in method.Parameters) Type(parameter.Type);
                    if (method.ReducedFrom != null) Type(method.ReducedFrom.ContainingType);
                    break;
                case IPropertySymbol property:
                    Type(property.Type);
                    foreach (var parameter in property.Parameters) Type(parameter.Type);
                    break;
                case IFieldSymbol field: Type(field.Type); break;
                case IEventSymbol @event: Type(@event.Type); break;
                case IParameterSymbol parameter: Type(parameter.Type); break;
                case ILocalSymbol local: Type(local.Type); break;
            }
        }
    }

    private void CheckType(string sourceModule, ITypeSymbol? type, Location location,
        Action<Diagnostic> report, HashSet<ITypeSymbol> visited)
    {
        if (type == null || !visited.Add(type)) return;
        switch (type)
        {
            case IArrayTypeSymbol array:
                CheckType(sourceModule, array.ElementType, location, report, visited);
                return;
            case IPointerTypeSymbol pointer:
                CheckType(sourceModule, pointer.PointedAtType, location, report, visited);
                return;
            case IFunctionPointerTypeSymbol functionPointer:
                CheckType(sourceModule, functionPointer.Signature.ReturnType, location, report, visited);
                foreach (var functionParameter in functionPointer.Signature.Parameters)
                    CheckType(sourceModule, functionParameter.Type, location, report, visited);
                return;
            case ITypeParameterSymbol typeParameter:
                foreach (var constraint in typeParameter.ConstraintTypes)
                    CheckType(sourceModule, constraint, location, report, visited);
                return;
            case INamedTypeSymbol named:
                // Anonymous projections have no authored namespace or stable metadata identity.
                // Their dependencies are their property types, including nested projections.
                if (named.IsAnonymousType)
                {
                    foreach (var property in named.GetMembers().OfType<IPropertySymbol>())
                        CheckType(sourceModule, property.Type, location, report, visited);
                    return;
                }
                var assembly = named.ContainingAssembly.Name;
                string? targetModule;
                if (_policy.Assemblies.Contains(assembly))
                {
                    targetModule = Module(named);
                    if (targetModule == null) Unknown(named, location, report);
                }
                else if (!_policy.ExternalAssemblyModules.TryGetValue(assembly, out targetModule))
                    Report(ArchitectureBoundaryAnalyzer.UncoveredProject, location, report, assembly);
                if (targetModule != null && sourceModule != targetModule && !_policy.AllowedEdges.Contains((sourceModule, targetModule)))
                    Report(ArchitectureBoundaryAnalyzer.ForbiddenDependency, location, report,
                        sourceModule, targetModule, TypeName(named.OriginalDefinition));
                foreach (var argument in named.TypeArguments)
                    CheckType(sourceModule, argument, location, report, visited);
                if (named.ContainingType != null)
                    CheckType(sourceModule, named.ContainingType, location, report, visited);
                return;
        }
    }

    private string? Module(INamedTypeSymbol type)
    {
        for (var current = type.OriginalDefinition; current != null; current = current.ContainingType)
            if (_policy.TypeModules.TryGetValue(TypeName(current), out var exact)) return exact;
        return _policy.NamespaceModules.TryGetValue(type.ContainingNamespace.ToDisplayString(), out var module)
            ? module : null;
    }

    private void Unknown(INamedTypeSymbol type, Location location, Action<Diagnostic> report) =>
        Report(ArchitectureBoundaryAnalyzer.UnknownOwnership, location, report, TypeName(type));

    private void Report(DiagnosticDescriptor descriptor, Location location, Action<Diagnostic> report,
        params object[] arguments)
    {
        var key = descriptor.Id + "|" + location.SourceTree?.FilePath + "|" + location.SourceSpan + "|" + string.Join("|", arguments);
        if (_reported.TryAdd(key, 0)) report(Diagnostic.Create(descriptor, location, arguments));
    }

    private static string TypeName(INamedTypeSymbol type) => type.ContainingType != null
        ? TypeName(type.ContainingType) + "." + type.MetadataName
        : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;
}
