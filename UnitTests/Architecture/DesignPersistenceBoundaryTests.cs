using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>The native document format must be usable without loading the presentation assembly.</summary>
public class DesignPersistenceBoundaryTests
{
    [Fact]
    public void NativeDocumentContract_IsOwnedByDataAccess()
    {
        typeof(DesignFileData).Assembly.ShouldBe(typeof(DesignMetadata).Assembly);
        typeof(DesignFileData).Namespace.ShouldStartWith("CAP_DataAccess.Persistence.");
    }

    [Fact]
    public void NativeDocumentContract_HasNoPresentationPropertyTypes()
    {
        var queue = new Queue<Type>(new[] { typeof(DesignFileData) });
        var visited = new HashSet<Type>();
        var presentationTypes = new List<string>();
        while (queue.TryDequeue(out var type))
        {
            if (!visited.Add(type)) continue;
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments()) queue.Enqueue(argument);
            if (type.IsArray) queue.Enqueue(type.GetElementType()!);
            if (type.Namespace?.StartsWith("CAP.Avalonia", StringComparison.Ordinal) == true)
                presentationTypes.Add(type.FullName!);
            if (type.Namespace?.StartsWith("CAP", StringComparison.Ordinal) != true) continue;
            foreach (var property in type.GetProperties()) queue.Enqueue(property.PropertyType);
        }
        presentationTypes.ShouldBeEmpty("persisted data may depend on contracts/domain data, never ViewModels or UI services");
    }
}
