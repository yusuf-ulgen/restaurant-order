using Xunit;

namespace RestaurantOrder.ArchitectureTests;

public class ArchitectureTests
{
    [Fact]
    public void Api_Should_Not_Depend_On_Worker()
    {
        var apiAssembly = typeof(RestaurantOrder.Api.Program).Assembly;
        var referencedAssemblies = apiAssembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Worker");
    }

    [Fact]
    public void Worker_Should_Not_Depend_On_Api()
    {
        var workerAssembly = typeof(RestaurantOrder.Worker.Worker).Assembly;
        var referencedAssemblies = workerAssembly.GetReferencedAssemblies();

        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Api");
    }
}
