using Xunit;

namespace RestaurantOrder.ArchitectureTests;

public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(RestaurantOrder.Domain.AssemblyReference).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(RestaurantOrder.Application.AssemblyReference).Assembly;
    private static readonly System.Reflection.Assembly InfrastructureAssembly = typeof(RestaurantOrder.Infrastructure.AssemblyReference).Assembly;
    private static readonly System.Reflection.Assembly ApiAssembly = typeof(RestaurantOrder.Api.Program).Assembly;
    private static readonly System.Reflection.Assembly WorkerAssembly = typeof(RestaurantOrder.Worker.Worker).Assembly;

    [Fact]
    public void Domain_Should_Not_Depend_On_Infrastructure()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Infrastructure");
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_Application()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Application");
    }

    [Fact]
    public void Domain_Should_Not_Depend_On_EfCore_Or_Npgsql_Or_AspNetCore()
    {
        var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name != null && a.Name.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(referencedAssemblies, a => a.Name != null && a.Name.StartsWith("Npgsql"));
        Assert.DoesNotContain(referencedAssemblies, a => a.Name != null && a.Name.StartsWith("Microsoft.AspNetCore"));
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Api_Or_Worker()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Api");
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Worker");
    }

    [Fact]
    public void Application_Should_Not_Depend_On_Infrastructure()
    {
        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Infrastructure");
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Api()
    {
        var referencedAssemblies = InfrastructureAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Api");
    }

    [Fact]
    public void Infrastructure_Should_Not_Depend_On_Worker()
    {
        var referencedAssemblies = InfrastructureAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Worker");
    }

    [Fact]
    public void Api_Should_Not_Depend_On_Worker()
    {
        var referencedAssemblies = ApiAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Worker");
    }

    [Fact]
    public void Worker_Should_Not_Depend_On_Api()
    {
        var referencedAssemblies = WorkerAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a => a.Name == "RestaurantOrder.Api");
    }

    [Fact]
    public void Layer_References_Should_Follow_Expected_Direction()
    {
        // Application depends on Domain
        var appRefs = ApplicationAssembly.GetReferencedAssemblies();
        Assert.Contains(appRefs, a => a.Name == "RestaurantOrder.Domain");

        // Infrastructure depends on Domain and Application
        var infraRefs = InfrastructureAssembly.GetReferencedAssemblies();
        Assert.Contains(infraRefs, a => a.Name == "RestaurantOrder.Domain");
        Assert.Contains(infraRefs, a => a.Name == "RestaurantOrder.Application");

        // Api composition root depends on Application and Infrastructure
        var apiRefs = ApiAssembly.GetReferencedAssemblies();
        Assert.Contains(apiRefs, a => a.Name == "RestaurantOrder.Application");
        Assert.Contains(apiRefs, a => a.Name == "RestaurantOrder.Infrastructure");
    }
}
