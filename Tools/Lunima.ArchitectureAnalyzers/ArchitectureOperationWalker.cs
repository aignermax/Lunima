using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Lunima.ArchitectureAnalyzers;

/// <summary>Includes inferred types and operator/conversion methods that have no written type name.</summary>
internal sealed class ArchitectureOperationWalker : OperationWalker
{
    private readonly ArchitectureDependencyCollector _collector;
    private readonly ISymbol _source;
    private readonly Action<Diagnostic> _report;

    public ArchitectureOperationWalker(ArchitectureDependencyCollector collector, ISymbol source,
        Action<Diagnostic> report) => (_collector, _source, _report) = (collector, source, report);

    public override void Visit(IOperation? operation)
    {
        if (operation == null) return;
        var location = operation.Syntax.GetLocation();
        _collector.CheckReference(_source, operation.Type, location, _report);
        ISymbol? member = operation switch
        {
            IInvocationOperation call => call.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor,
            IMemberReferenceOperation reference => reference.Member,
            IConversionOperation conversion => conversion.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IIncrementOrDecrementOperation increment => increment.OperatorMethod,
            ICompoundAssignmentOperation assignment => assignment.OperatorMethod,
            ITypeOfOperation typeOf => typeOf.TypeOperand,
            _ => null
        };
        _collector.CheckReference(_source, member, location, _report);
        base.Visit(operation);
    }
}
