using RestaurantOrder.Application.Orders;
using Xunit;

namespace RestaurantOrder.UnitTests.Orders;

public class OrderFingerprintGeneratorTests
{
    private readonly Guid _sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _menuItemId1 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _menuItemId2 = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _modGroup1 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly Guid _modOption1 = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private readonly Guid _modOption2 = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void Fingerprint_IsDeterministic_AndExact64Chars()
    {
        var items = new List<CustomerOrderItemRequest>
        {
            new(_menuItemId1, null, 2, "Test note", null, 4000)
        };

        var fp1 = OrderFingerprintGenerator.ComputeCustomerOrderFingerprint(_sessionId, items);
        var fp2 = OrderFingerprintGenerator.ComputeCustomerOrderFingerprint(_sessionId, items);

        Assert.Equal(64, fp1.Length);
        Assert.Equal(fp1, fp2);
        Assert.Equal(fp1.ToLowerInvariant(), fp1);
    }

    [Fact]
    public void Fingerprint_StaffAndCustomer_ProduceIdenticalHash_ForIdenticalPayload()
    {
        var customerItems = new List<CustomerOrderItemRequest>
        {
            new(_menuItemId1, null, 2, "No onion", new List<CustomerSelectedModifierRequest>
            {
                new(_modGroup1, _modOption1)
            }, 5000)
        };

        var staffItems = new List<StaffOrderItemRequest>
        {
            new(_menuItemId1, null, 2, "No onion", new List<StaffSelectedModifierRequest>
            {
                new(_modGroup1, _modOption1)
            }, 5000)
        };

        var custFp = OrderFingerprintGenerator.ComputeCustomerOrderFingerprint(_sessionId, customerItems, "policy-1", 5000);
        var staffFp = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, staffItems, "policy-1", 5000);

        Assert.Equal(custFp, staffFp);
    }

    [Fact]
    public void Fingerprint_ItemOrderReversed_ProducesIdenticalFingerprint()
    {
        var itemA = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);
        var itemB = new StaffOrderItemRequest(_menuItemId2, null, 1, null, null, 3000);

        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA, itemB });
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemB, itemA });

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_ModifierOrderReversed_ProducesIdenticalFingerprint()
    {
        var item1 = new StaffOrderItemRequest(_menuItemId1, null, 1, null, new[]
        {
            new StaffSelectedModifierRequest(_modGroup1, _modOption1),
            new StaffSelectedModifierRequest(_modGroup1, _modOption2),
        }, 5000);

        var item2 = new StaffOrderItemRequest(_menuItemId1, null, 1, null, new[]
        {
            new StaffSelectedModifierRequest(_modGroup1, _modOption2),
            new StaffSelectedModifierRequest(_modGroup1, _modOption1),
        }, 5000);

        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { item1 });
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { item2 });

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_NullNote_AndLiteralNullStringNote_ProduceDifferentHash()
    {
        var itemNull = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);
        var itemLiteralNull = new StaffOrderItemRequest(_menuItemId1, null, 1, "null", null, 2000);

        var fpNull = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemNull });
        var fpLiteralNull = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemLiteralNull });

        Assert.NotEqual(fpNull, fpLiteralNull);
    }

    [Fact]
    public void Fingerprint_WhitespaceNote_NormalizesIdenticallyToNull()
    {
        var itemNull = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);
        var itemEmpty = new StaffOrderItemRequest(_menuItemId1, null, 1, "", null, 2000);
        var itemSpaces = new StaffOrderItemRequest(_menuItemId1, null, 1, "   \t  ", null, 2000);

        var fpNull = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemNull });
        var fpEmpty = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemEmpty });
        var fpSpaces = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemSpaces });

        Assert.Equal(fpNull, fpEmpty);
        Assert.Equal(fpNull, fpSpaces);
    }

    [Fact]
    public void Fingerprint_SpecialCharactersInNote_DoNotCauseCollisions()
    {
        var noteA = "| , [ ] : ; > \" \\";
        var noteB = "Different note with | , [ ]";

        var itemA = new StaffOrderItemRequest(_menuItemId1, null, 1, noteA, null, 2000);
        var itemB = new StaffOrderItemRequest(_menuItemId1, null, 1, noteB, null, 2000);

        var fpA = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA });
        var fpB = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemB });

        Assert.NotEqual(fpA, fpB);
        Assert.Equal(64, fpA.Length);
        Assert.Equal(64, fpB.Length);
    }

    [Fact]
    public void Fingerprint_CustomerNoteChanged_ProducesDifferentFingerprint()
    {
        var item1 = new StaffOrderItemRequest(_menuItemId1, null, 1, "Az tuzlu", null, 2000);
        var item2 = new StaffOrderItemRequest(_menuItemId1, null, 1, "Tuzsuz", null, 2000);

        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { item1 });
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { item2 });

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_ExpectedPriceChanged_ProducesDifferentFingerprint()
    {
        var item1 = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);
        var item2 = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2500);

        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { item1 });
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { item2 });

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_PreservesDuplicateItems_DistinctFromSingleItem()
    {
        var itemA = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);

        // One line with itemA
        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA });

        // Two lines with itemA (duplicate lines)
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA, itemA });

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_ExpectedTotalChanged_ProducesDifferentFingerprint()
    {
        var itemA = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);

        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA }, "policy-1", 2000);
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA }, "policy-1", 2500);

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void Fingerprint_PricingPolicyFingerprintChanged_ProducesDifferentFingerprint()
    {
        var itemA = new StaffOrderItemRequest(_menuItemId1, null, 1, null, null, 2000);

        var fp1 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA }, "policy-alpha", 2000);
        var fp2 = OrderFingerprintGenerator.ComputeStaffOrderFingerprint(_sessionId, new[] { itemA }, "policy-beta", 2000);

        Assert.NotEqual(fp1, fp2);
    }
}
