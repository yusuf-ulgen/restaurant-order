using RestaurantOrder.Api.Floor;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Domain.Floor;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class FloorBatchLayoutUnitTests
{
    [Fact]
    public void TableLayoutBatchItem_InstantiatesWithProvidedValues()
    {
        var tableId = Guid.NewGuid();
        var token = Guid.NewGuid();

        var item = new TableLayoutBatchItem(
            TableId: tableId,
            PositionX: 120,
            PositionY: 240,
            Width: 100,
            Height: 80,
            RotationDegrees: 90,
            Shape: "Rectangle",
            ConcurrencyToken: token);

        Assert.Equal(tableId, item.TableId);
        Assert.Equal(120, item.PositionX);
        Assert.Equal(240, item.PositionY);
        Assert.Equal(100, item.Width);
        Assert.Equal(80, item.Height);
        Assert.Equal(90, item.RotationDegrees);
        Assert.Equal("Rectangle", item.Shape);
        Assert.Equal(token, item.ConcurrencyToken);
    }

    [Fact]
    public void BatchUpdateTableLayoutRequest_HoldsCollectionOfItems()
    {
        var item1 = new TableLayoutBatchItem(Guid.NewGuid(), 10, 20, 100, 100, 0, "Square");
        var item2 = new TableLayoutBatchItem(Guid.NewGuid(), 50, 60, 80, 80, 45, "Round");

        var request = new BatchUpdateTableLayoutRequest(new[] { item1, item2 });

        Assert.NotNull(request.Items);
        Assert.Equal(2, request.Items.Count);
        Assert.Contains(item1, request.Items);
        Assert.Contains(item2, request.Items);
    }

    [Fact]
    public void BatchUpdateTableLayoutApiRequest_MapsToDomainDtoItems()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var c1 = Guid.NewGuid();

        var apiItem1 = new TableLayoutBatchApiItem(t1, 100, 200, 120, 120, 0, "Square", c1);
        var apiItem2 = new TableLayoutBatchApiItem(t2, 300, 400, 80, 80, 0, "Round", null);

        var apiRequest = new BatchUpdateTableLayoutApiRequest(new[] { apiItem1, apiItem2 });

        Assert.Equal(2, apiRequest.Items.Count);
        Assert.Equal("Square", apiRequest.Items[0].Shape);
        Assert.Equal("Round", apiRequest.Items[1].Shape);
        Assert.Equal(c1, apiRequest.Items[0].ConcurrencyToken);
        Assert.Null(apiRequest.Items[1].ConcurrencyToken);
    }

    [Theory]
    [InlineData("Square", TableShape.Square)]
    [InlineData("Round", TableShape.Round)]
    [InlineData("Rectangle", TableShape.Rectangle)]
    public void TableShape_ParsesValidShapeNamesCaseInsensitively(string shapeName, TableShape expected)
    {
        var parsed = Enum.TryParse<TableShape>(shapeName, ignoreCase: true, out var shape);
        Assert.True(parsed);
        Assert.Equal(expected, shape);
    }

    [Theory]
    [InlineData("Triangle")]
    [InlineData("Oval")]
    [InlineData("Hexagon")]
    [InlineData("")]
    public void TableShape_RejectsInvalidShapeNames(string shapeName)
    {
        var parsed = Enum.TryParse<TableShape>(shapeName, ignoreCase: true, out _);
        Assert.False(parsed);
    }
}
