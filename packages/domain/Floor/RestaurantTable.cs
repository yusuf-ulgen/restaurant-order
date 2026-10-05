using System.Text.RegularExpressions;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Tenants;

namespace RestaurantOrder.Domain.Floor;

/// <summary>
/// Physical dining table entity located within a DiningArea of a restaurant branch.
/// Manages table identification, seating capacity, canvas layout dimensions, and active lifecycle.
/// Operational states (e.g. Occupied, BillRequested) are derived from dining sessions in later phases.
/// </summary>
public class RestaurantTable
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    public const int MinCapacity = 1;
    public const int MaxCapacity = 100;
    public const int MinCoordinate = 0;
    public const int MaxCoordinate = 10000;
    public const int MinDimension = 10;
    public const int MaxDimension = 5000;
    public const int MinRotation = 0;
    public const int MaxRotation = 359;
    public const int InitialQrVersion = 1;

    public RestaurantTableId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public BranchId BranchId { get; private set; }
    public DiningAreaId DiningAreaId { get; private set; }

    public string TableNumber { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Capacity { get; private set; }

    public int PositionX { get; private set; }
    public int PositionY { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int RotationDegrees { get; private set; }
    public TableShape Shape { get; private set; }

    public bool IsActive { get; private set; }
    public int QrVersion { get; private set; }
    public Guid ConcurrencyToken { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    // Parameterless constructor for EF Core persistence materialization
    private RestaurantTable()
    {
    }

    public static RestaurantTable Create(
        TenantId tenantId,
        BranchId branchId,
        DiningAreaId diningAreaId,
        string tableNumber,
        string name,
        int capacity,
        int positionX = 0,
        int positionY = 0,
        int width = 100,
        int height = 100,
        int rotationDegrees = 0,
        TableShape shape = TableShape.Square,
        RestaurantTableId? id = null)
    {
        var validatedNumber = ValidateTableNumber(tableNumber);
        var validatedName = ValidateName(name);
        ValidateCapacity(capacity);
        ValidateLayout(positionX, positionY, width, height, rotationDegrees, shape);

        var tableId = id ?? RestaurantTableId.New();

        return new RestaurantTable
        {
            Id = tableId,
            TenantId = tenantId,
            BranchId = branchId,
            DiningAreaId = diningAreaId,
            TableNumber = validatedNumber,
            Name = validatedName,
            Capacity = capacity,
            PositionX = positionX,
            PositionY = positionY,
            Width = width,
            Height = height,
            RotationDegrees = rotationDegrees,
            Shape = shape,
            IsActive = true,
            QrVersion = InitialQrVersion,
            ConcurrencyToken = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Update(
        string tableNumber,
        string name,
        int capacity,
        DiningAreaId diningAreaId)
    {
        TableNumber = ValidateTableNumber(tableNumber);
        Name = ValidateName(name);
        ValidateCapacity(capacity);
        DiningAreaId = diningAreaId;
        Capacity = capacity;

        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void UpdateLayout(
        int positionX,
        int positionY,
        int width,
        int height,
        int rotationDegrees,
        TableShape shape)
    {
        ValidateLayout(positionX, positionY, width, height, rotationDegrees, shape);

        PositionX = positionX;
        PositionY = positionY;
        Width = width;
        Height = height;
        RotationDegrees = rotationDegrees;
        Shape = shape;

        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Activate()
    {
        if (IsActive)
        {
            throw new DomainException("Table is already active.");
        }

        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            throw new DomainException("Table is already inactive.");
        }

        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    public void BumpQrVersion()
    {
        QrVersion++;
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    private static string ValidateTableNumber(string tableNumber)
    {
        if (string.IsNullOrWhiteSpace(tableNumber))
        {
            throw new DomainException("TableNumber cannot be empty.");
        }

        var trimmed = tableNumber.Trim();
        if (trimmed.Length > 50)
        {
            throw new DomainException("TableNumber cannot exceed 50 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed) ||
            trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("TableNumber cannot contain HTML tags or script injection.");
        }

        return trimmed;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 100)
        {
            throw new DomainException("Name cannot exceed 100 characters.");
        }

        if (HtmlTagRegex.IsMatch(trimmed) ||
            trimmed.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("<script", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Name cannot contain HTML tags or script injection.");
        }

        return trimmed;
    }

    private static void ValidateCapacity(int capacity)
    {
        if (capacity < MinCapacity || capacity > MaxCapacity)
        {
            throw new DomainException($"Capacity must be between {MinCapacity} and {MaxCapacity}.");
        }
    }

    private static void ValidateLayout(
        int positionX,
        int positionY,
        int width,
        int height,
        int rotationDegrees,
        TableShape shape)
    {
        if (positionX < MinCoordinate || positionX > MaxCoordinate)
        {
            throw new DomainException($"PositionX must be between {MinCoordinate} and {MaxCoordinate}.");
        }

        if (positionY < MinCoordinate || positionY > MaxCoordinate)
        {
            throw new DomainException($"PositionY must be between {MinCoordinate} and {MaxCoordinate}.");
        }

        if (width < MinDimension || width > MaxDimension)
        {
            throw new DomainException($"Width must be between {MinDimension} and {MaxDimension}.");
        }

        if (height < MinDimension || height > MaxDimension)
        {
            throw new DomainException($"Height must be between {MinDimension} and {MaxDimension}.");
        }

        if (rotationDegrees < MinRotation || rotationDegrees > MaxRotation)
        {
            throw new DomainException($"RotationDegrees must be between {MinRotation} and {MaxRotation}.");
        }

        if (!Enum.IsDefined(typeof(TableShape), shape))
        {
            throw new DomainException($"Invalid TableShape value: {shape}.");
        }
    }
}
