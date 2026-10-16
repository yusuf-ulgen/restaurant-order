using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using Xunit;

namespace RestaurantOrder.UnitTests.Floor;

public class ServiceRequestDomainUnitTests
{
    private readonly TenantId _tenantId = TenantId.New();
    private readonly BranchId _branchId = BranchId.New();
    private readonly DiningSessionId _sessionId = DiningSessionId.New();
    private readonly RestaurantTableId _tableId = RestaurantTableId.New();

    [Theory]
    [InlineData(ServiceRequestType.Waiter)]
    [InlineData(ServiceRequestType.Water)]
    [InlineData(ServiceRequestType.Napkin)]
    [InlineData(ServiceRequestType.Assistance)]
    public void Create_ValidTypes_InitializesInOpenState(ServiceRequestType type)
    {
        var key = "test-key-123";
        var note = "Lütfen 2 bardak su getirebilir misiniz?";

        var request = ServiceRequest.Create(
            _tenantId,
            _branchId,
            _sessionId,
            _tableId,
            type,
            note,
            key);

        Assert.NotEqual(Guid.Empty, request.Id.Value);
        Assert.Equal(_tenantId, request.TenantId);
        Assert.Equal(_branchId, request.BranchId);
        Assert.Equal(_sessionId, request.DiningSessionId);
        Assert.Equal(_tableId, request.TableId);
        Assert.Equal(type, request.RequestType);
        Assert.Equal(ServiceRequestStatus.Open, request.Status);
        Assert.Equal(note, request.Note);
        Assert.Equal(key, request.IdempotencyKey);
        Assert.NotEmpty(request.RequestFingerprint);
        Assert.NotEqual(Guid.Empty, request.ConcurrencyToken);
        Assert.Null(request.AcknowledgedAtUtc);
        Assert.Null(request.CompletedAtUtc);
    }

    [Fact]
    public void Create_InvalidType_ThrowsDomainException()
    {
        var invalidType = (ServiceRequestType)99;
        Assert.Throws<DomainException>(() =>
            ServiceRequest.Create(
                _tenantId,
                _branchId,
                _sessionId,
                _tableId,
                invalidType,
                null,
                "key-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyIdempotencyKey_ThrowsDomainException(string? emptyKey)
    {
        Assert.Throws<DomainException>(() =>
            ServiceRequest.Create(
                _tenantId,
                _branchId,
                _sessionId,
                _tableId,
                ServiceRequestType.Waiter,
                null,
                emptyKey!));
    }

    [Fact]
    public void Create_IdempotencyKeyTooLong_ThrowsDomainException()
    {
        var longKey = new string('k', ServiceRequest.MaxIdempotencyKeyLength + 1);
        Assert.Throws<DomainException>(() =>
            ServiceRequest.Create(
                _tenantId,
                _branchId,
                _sessionId,
                _tableId,
                ServiceRequestType.Water,
                null,
                longKey));
    }

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("Lütfen bakın <img src=x onerror=alert(1)>")]
    [InlineData("Acil <b>yardım</b>")]
    public void ValidateNote_HtmlTags_ThrowsDomainException(string htmlNote)
    {
        Assert.Throws<DomainException>(() =>
            ServiceRequest.ValidateAndSanitizeNote(htmlNote));
    }

    [Fact]
    public void ValidateNote_ControlCharacters_ThrowsDomainException()
    {
        var noteWithNullByte = "Su lütfen\0zararlı";
        Assert.Throws<DomainException>(() =>
            ServiceRequest.ValidateAndSanitizeNote(noteWithNullByte));

        var noteWithBell = "Garson\u0007lütfen";
        Assert.Throws<DomainException>(() =>
            ServiceRequest.ValidateAndSanitizeNote(noteWithBell));
    }

    [Fact]
    public void ValidateNote_ExceedsMaxLength_ThrowsDomainException()
    {
        var longNote = new string('a', ServiceRequest.MaxNoteLength + 1);
        Assert.Throws<DomainException>(() =>
            ServiceRequest.ValidateAndSanitizeNote(longNote));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Normal not  ", "Normal not")]
    public void ValidateNote_NormalizesWhitespace(string? input, string? expected)
    {
        var result = ServiceRequest.ValidateAndSanitizeNote(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Cancel_OpenRequest_TransitionsToCancelled()
    {
        var request = ServiceRequest.Create(
            _tenantId,
            _branchId,
            _sessionId,
            _tableId,
            ServiceRequestType.Waiter,
            null,
            "key-123");

        var originalToken = request.ConcurrencyToken;
        request.Cancel();

        Assert.Equal(ServiceRequestStatus.Cancelled, request.Status);
        Assert.NotEqual(originalToken, request.ConcurrencyToken);
        Assert.NotNull(request.UpdatedAtUtc);
    }

    [Fact]
    public void Cancel_AlreadyCancelled_ThrowsDomainException()
    {
        var request = ServiceRequest.Create(
            _tenantId,
            _branchId,
            _sessionId,
            _tableId,
            ServiceRequestType.Waiter,
            null,
            "key-123");

        request.Cancel();

        Assert.Throws<DomainException>(() => request.Cancel());
    }

    [Fact]
    public void Cancel_AcknowledgedOrCompleted_ThrowsDomainException()
    {
        var req1 = ServiceRequest.Create(_tenantId, _branchId, _sessionId, _tableId, ServiceRequestType.Water, null, "k1");
        req1.Acknowledge();
        Assert.Throws<DomainException>(() => req1.Cancel());

        var req2 = ServiceRequest.Create(_tenantId, _branchId, _sessionId, _tableId, ServiceRequestType.Napkin, null, "k2");
        req2.Complete();
        Assert.Throws<DomainException>(() => req2.Cancel());
    }

    [Fact]
    public void Fingerprint_DeterministicAndCollisionResistant()
    {
        var fp1 = ServiceRequest.ComputeFingerprint(_tenantId, _branchId, _sessionId, _tableId, ServiceRequestType.Waiter, "Not 1");
        var fp2 = ServiceRequest.ComputeFingerprint(_tenantId, _branchId, _sessionId, _tableId, ServiceRequestType.Waiter, "Not 1");
        var fp3 = ServiceRequest.ComputeFingerprint(_tenantId, _branchId, _sessionId, _tableId, ServiceRequestType.Water, "Not 1");
        var fp4 = ServiceRequest.ComputeFingerprint(_tenantId, _branchId, _sessionId, _tableId, ServiceRequestType.Waiter, "Not 2");

        Assert.Equal(fp1, fp2);
        Assert.True(ServiceRequest.MatchesFingerprint(fp1, fp2));

        Assert.NotEqual(fp1, fp3);
        Assert.False(ServiceRequest.MatchesFingerprint(fp1, fp3));

        Assert.NotEqual(fp1, fp4);
        Assert.False(ServiceRequest.MatchesFingerprint(fp1, fp4));
    }
}
