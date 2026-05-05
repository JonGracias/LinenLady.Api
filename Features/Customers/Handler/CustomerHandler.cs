namespace LinenLady.API.Customers.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Customers.Sql;

// ── Exceptions ───────────────────────────────────────────────

public sealed class CustomerNotFoundException    : Exception { public CustomerNotFoundException(string m)    : base(m) {} }
public sealed class EmailNotVerifiedException    : Exception { public EmailNotVerifiedException(string m)    : base(m) {} }
public sealed class ItemAlreadyReservedException : Exception { public ItemAlreadyReservedException(string m) : base(m) {} }

/// <summary>
/// Customer is trying to reserve an item they already have an active
/// reservation for. Distinct from ItemAlreadyReservedException (which fires
/// when *someone else* holds the item) so the frontend can route the user
/// to their existing reservation instead of showing an error.
/// </summary>
public sealed class ItemAlreadyReservedByYouException : Exception
{
    public int ReservationId { get; }
    public ItemAlreadyReservedByYouException(int reservationId, string m)
        : base(m) { ReservationId = reservationId; }
}

public sealed class ItemNotFoundException        : Exception { public ItemNotFoundException(string m)        : base(m) {} }
public sealed class ReservationNotFoundException : Exception { public ReservationNotFoundException(string m) : base(m) {} }
public sealed class ReservationConflictException : Exception { public ReservationConflictException(string m) : base(m) {} }

// ─────────────────────────────────────────────────────────────

public sealed class SyncCustomerHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<SyncCustomerHandler> _log;

    public SyncCustomerHandler(ICustomerRepository repo, ILogger<SyncCustomerHandler> log)
    {
        _repo = repo;
        _log = log;
    }

    /// <summary>
    /// Upserts the customer record. Identity fields (<paramref name="clerkUserId"/>,
    /// <paramref name="email"/>, <paramref name="isEmailVerified"/>) come from the
    /// validated Clerk JWT, not from <paramref name="req"/>, so the caller can't
    /// spoof someone else's identity or mark their own email verified.
    /// </summary>
    public async Task<CustomerDto> HandleAsync(
        string clerkUserId,
        string email,
        bool isEmailVerified,
        UpsertCustomerRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clerkUserId))
            throw new ArgumentException("ClerkUserId is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.");

        var customer = await _repo.UpsertAsync(
            clerkUserId, email, isEmailVerified, req);

        _log.LogInformation("Customer synced: {Id} ({Email})", customer.CustomerId, customer.Email);
        return customer;
    }
}

// ─────────────────────────────────────────────────────────────

public sealed class GetMyProfileHandler
{
    private readonly ICustomerRepository _repo;
    public GetMyProfileHandler(ICustomerRepository repo) => _repo = repo;

    /// <summary>
    /// Returns the customer's profile: identity, addresses, preferences.
    /// Reservations are NOT included — the frontend loads basket data from
    /// /api/customers/me/basket instead, which projects the new ReservationDto
    /// shape (Active/Expired statuses + canReAdd flag).
    /// </summary>
    public async Task<MyProfileResult> HandleAsync(string clerkUserId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var addresses    = await _repo.GetAddressesAsync(customer.CustomerId);
        var preferences  = await _repo.GetPreferencesAsync(customer.CustomerId);

        return new MyProfileResult(customer, addresses, preferences);
    }
}

public record MyProfileResult(
    CustomerDto                  Customer,
    List<CustomerAddressDto>     Addresses,
    List<CustomerPreferenceDto>  Preferences
);

// ─────────────────────────────────────────────────────────────

public sealed class UpdateProfileHandler
{
    private readonly ICustomerRepository _repo;
    public UpdateProfileHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<CustomerDto> HandleAsync(
        string clerkUserId, UpdateCustomerRequest req, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        return await _repo.UpdateAsync(customer.CustomerId, req)
            ?? throw new CustomerNotFoundException("Update failed.");
    }
}

// ─────────────────────────────────────────────────────────────

public sealed class UpsertAddressHandler
{
    private readonly ICustomerRepository _repo;
    public UpsertAddressHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<CustomerAddressDto> HandleAsync(
        string clerkUserId, UpsertAddressRequest req, int? addressId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Street1) || string.IsNullOrWhiteSpace(req.City)
            || string.IsNullOrWhiteSpace(req.State) || string.IsNullOrWhiteSpace(req.Zip))
            throw new ArgumentException("Street, City, State, and Zip are required.");

        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        return await _repo.UpsertAddressAsync(customer.CustomerId, req, addressId);
    }
}

// ─────────────────────────────────────────────────────────────

public sealed class DeleteAddressHandler
{
    private readonly ICustomerRepository _repo;
    public DeleteAddressHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<bool> HandleAsync(
        string clerkUserId, int addressId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        return await _repo.DeleteAddressAsync(customer.CustomerId, addressId);
    }
}

// ─────────────────────────────────────────────────────────────

public sealed class SetPreferencesHandler
{
    private readonly ICustomerRepository _repo;
    public SetPreferencesHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<List<CustomerPreferenceDto>> HandleAsync(
        string clerkUserId, SetPreferencesRequest req, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        await _repo.SetPreferencesAsync(customer.CustomerId, req.Categories);
        return await _repo.GetPreferencesAsync(customer.CustomerId);
    }
}

// ─────────────────────────────────────────────────────────────

public sealed class ExpireReservationsHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<ExpireReservationsHandler> _log;

    public ExpireReservationsHandler(
        ICustomerRepository repo,
        ILogger<ExpireReservationsHandler> log)
    {
        _repo = repo;
        _log = log;
    }

    public async Task<int> HandleAsync(CancellationToken ct)
    {
        var count = await _repo.ExpireReservationsAsync();
        if (count > 0)
            _log.LogInformation("Expired {Count} reservation(s).", count);
        return count;
    }
}

// ─────────────────────────────────────────────────────────────

public sealed class MessageHandler
{
    private readonly ICustomerRepository _repo;
    public MessageHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<List<MessageDto>> GetAsync(string clerkUserId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        await _repo.MarkMessagesReadAsync(customer.CustomerId);
        return await _repo.GetMessagesAsync(customer.CustomerId);
    }

    public async Task<MessageDto> SendAsync(
        string clerkUserId, SendMessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
            throw new ArgumentException("Message body cannot be empty.");

        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        return await _repo.SendMessageAsync(customer.CustomerId, req, "Inbound");
    }

    /// <summary>
    /// Customer-side unread count — number of admin replies the customer
    /// hasn't yet viewed. Drives the badge on the storefront Account link.
    /// </summary>
    public async Task<UnreadCountDto> GetUnreadCountAsync(string clerkUserId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var n = await _repo.GetUnreadOutboundCountAsync(customer.CustomerId);
        return new UnreadCountDto(n);
    }
}

// ─────────────────────────────────────────────────────────────
//
// Admin-side messaging handlers. These mirror the customer handlers but
// operate on a customerId path parameter instead of resolving via the
// caller's Clerk identity. The Admin authorization policy gates access.

public sealed class GetConversationsHandler
{
    private readonly ICustomerRepository _repo;
    public GetConversationsHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<List<ConversationSummaryDto>> HandleAsync(int take, CancellationToken ct)
        => await _repo.GetConversationsAsync(take);
}

public sealed class GetConversationThreadHandler
{
    private readonly ICustomerRepository _repo;
    public GetConversationThreadHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<List<MessageDto>> HandleAsync(int customerId, bool markRead, CancellationToken ct)
    {
        if (!await _repo.CustomerExistsAsync(customerId))
            throw new CustomerNotFoundException("Customer not found.");

        if (markRead)
            await _repo.MarkInboundMessagesReadAsync(customerId);

        return await _repo.GetMessagesAsync(customerId);
    }
}

public sealed class AdminSendMessageHandler
{
    private readonly ICustomerRepository _repo;
    public AdminSendMessageHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<MessageDto> HandleAsync(
        int customerId, AdminSendMessageRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
            throw new ArgumentException("Message body cannot be empty.");

        if (!await _repo.CustomerExistsAsync(customerId))
            throw new CustomerNotFoundException("Customer not found.");

        return await _repo.SendMessageAsync(
            customerId,
            new SendMessageRequest(req.Body, req.ReservationId, req.OrderId),
            direction: "Outbound");
    }
}

public sealed class GetTotalUnreadInboundHandler
{
    private readonly ICustomerRepository _repo;
    public GetTotalUnreadInboundHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<UnreadCountDto> HandleAsync(CancellationToken ct)
    {
        var n = await _repo.GetTotalUnreadInboundCountAsync();
        return new UnreadCountDto(n);
    }
}