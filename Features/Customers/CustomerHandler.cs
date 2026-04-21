// Application/Customers/CustomerHandler.cs
using Microsoft.Data.SqlClient;
using LinenLady.API.Customer.Sql;
using LinenLady.API.Square;
using LinenLady.API.Contracts;

namespace LinenLady.API.Customers.Handler;

// ── Exceptions used for HTTP mapping in the Function layer ───

public sealed class CustomerNotFoundException  : Exception { public CustomerNotFoundException(string m) : base(m) {} }
public sealed class EmailNotVerifiedException  : Exception { public EmailNotVerifiedException(string m)  : base(m) {} }
public sealed class ItemAlreadyReservedException : Exception { public ItemAlreadyReservedException(string m) : base(m) {} }
public sealed class ItemNotFoundException      : Exception { public ItemNotFoundException(string m)      : base(m) {} }
public sealed class ReservationNotFoundException : Exception { public ReservationNotFoundException(string m) : base(m) {} }
public sealed class ReservationConflictException : Exception { public ReservationConflictException(string m) : base(m) {} }

// ─────────────────────────────────────────────────────────────

/// <summary>Syncs a Clerk user into cust.Customer (called on sign-in).</summary>
public sealed class SyncCustomerHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<SyncCustomerHandler> _log;

    public SyncCustomerHandler(ICustomerRepository repo, ILogger<SyncCustomerHandler> log)
    {
        _repo = repo;
        _log  = log;
    }

    public async Task<CustomerDto> HandleAsync(UpsertCustomerRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClerkUserId))
            throw new ArgumentException("ClerkUserId is required.");
        if (string.IsNullOrWhiteSpace(req.Email))
            throw new ArgumentException("Email is required.");

        var customer = await _repo.UpsertAsync(req);
        _log.LogInformation("Customer synced: {Id} ({Email})", customer.CustomerId, customer.Email);
        return customer;
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>Returns full profile — customer, addresses, preferences, reservations.</summary>
public sealed class GetMyProfileHandler
{
    private readonly ICustomerRepository _repo;

    public GetMyProfileHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<MyProfileResult> HandleAsync(string clerkUserId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var addresses    = await _repo.GetAddressesAsync(customer.CustomerId);
        var preferences  = await _repo.GetPreferencesAsync(customer.CustomerId);
        var reservations = await _repo.GetCustomerReservationsAsync(customer.CustomerId);

        return new MyProfileResult(customer, addresses, preferences, reservations);
    }
}

public record MyProfileResult(
    CustomerDto                  Customer,
    List<CustomerAddressDto>     Addresses,
    List<CustomerPreferenceDto>  Preferences,
    List<ReservationDto>         Reservations
);

// ─────────────────────────────────────────────────────────────

/// <summary>Updates name / phone on the customer record.</summary>
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

/// <summary>Add or update a shipping address.</summary>
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

/// <summary>Delete a shipping address.</summary>
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

/// <summary>Replace all new-arrival alert preferences for the customer.</summary>
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

/// <summary>
/// Creates a reservation, immediately generates a Square payment link,
/// and logs notifications. Core business logic for the reserve flow.
/// </summary>
public sealed class CreateReservationHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ISquareService      _square;
    private readonly ILogger<CreateReservationHandler> _log;

    public CreateReservationHandler(
        ICustomerRepository repo,
        ISquareService square,
        ILogger<CreateReservationHandler> log)
    {
        _repo   = repo;
        _square = square;
        _log    = log;
    }

    public async Task<ReservationDto> HandleAsync(
        string clerkUserId, CreateReservationRequest req, CancellationToken ct)
    {
        // 1) Resolve customer
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        if (!customer.IsEmailVerified)
            throw new EmailNotVerifiedException(
                "Email verification required before reserving an item.");

        // 2) Check item is not already held
        var alreadyReserved = await _repo.IsItemReservedAsync(req.InventoryId);
        if (alreadyReserved)
            throw new ItemAlreadyReservedException(
                "This item is currently reserved by another customer.");

        // 3) Fetch item price
        int amountCents = await GetItemPriceCentsAsync(req.InventoryId, ct);

        // 4) Create reservation row (status = Pending, expires +48h)
        var reservation = await _repo.CreateReservationAsync(
            customer.CustomerId, req, amountCents);

        // 5) Generate Square payment link — non-fatal on failure
        try
        {
            var link = await _square.CreatePaymentLinkAsync(
                reservationId: reservation.ReservationId,
                itemName:      reservation.ItemName ?? "Linen Lady Item",
                itemSku:       reservation.ItemSku  ?? "",
                amountCents:   amountCents,
                customerEmail: customer.Email,
                customerName:  $"{customer.FirstName} {customer.LastName}".Trim()
            );

            reservation = await _repo.SetPaymentLinkAsync(
                reservation.ReservationId, link) ?? reservation;

            await _repo.LogNotificationAsync(
                customer.CustomerId, reservation.ReservationId, "PaymentLinkSent", true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Square payment link generation failed for reservation {Id} (non-fatal).",
                reservation.ReservationId);
            await _repo.LogNotificationAsync(
                customer.CustomerId, reservation.ReservationId,
                "PaymentLinkSent", false, ex.Message);
        }

        await _repo.LogNotificationAsync(
            customer.CustomerId, reservation.ReservationId, "ReservationConfirmed", true);

        _log.LogInformation(
            "Reservation {Id} created for customer {CustomerId}, item {InventoryId}.",
            reservation.ReservationId, customer.CustomerId, req.InventoryId);

        return reservation;
    }

    private static async Task<int> GetItemPriceCentsAsync(int inventoryId, CancellationToken ct)
    {
        var connStr = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING")
            ?? throw new InvalidOperationException("Missing SQL_CONNECTION_STRING.");

        using var conn = new SqlConnection(connStr);
        await conn.OpenAsync(ct);

        using var cmd = new SqlCommand(
            """
            SELECT UnitPriceCents FROM inv.Inventory
            WHERE InventoryId = @Id AND IsActive = 1 AND IsDeleted = 0
            """, conn);
        cmd.Parameters.Add("@Id", System.Data.SqlDbType.Int).Value = inventoryId;

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null or DBNull)
            throw new ItemNotFoundException($"Item {inventoryId} not found or unavailable.");

        return Convert.ToInt32(result);
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>Cancel a reservation (customer-initiated).</summary>
public sealed class CancelReservationHandler
{
    private readonly ICustomerRepository _repo;

    public CancelReservationHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<ReservationDto> HandleAsync(
        string clerkUserId, int reservationId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var reservation = await _repo.GetReservationAsync(reservationId)
            ?? throw new ReservationNotFoundException("Reservation not found.");

        if (reservation.CustomerId != customer.CustomerId)
            throw new ReservationNotFoundException("Reservation not found.");

        if (reservation.Status is "Completed" or "Expired")
            throw new ReservationConflictException(
                $"A {reservation.Status} reservation cannot be cancelled.");

        return await _repo.UpdateReservationStatusAsync(reservationId, "Cancelled")
            ?? throw new ReservationNotFoundException("Update failed.");
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>Handles inbound Square webhook — marks reservation Completed on payment.</summary>
public sealed class SquareWebhookHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<SquareWebhookHandler> _log;

    public SquareWebhookHandler(ICustomerRepository repo, ILogger<SquareWebhookHandler> log)
    {
        _repo = repo;
        _log  = log;
    }

    public async Task HandleAsync(string rawBody, CancellationToken ct)
    {
        using var doc  = System.Text.Json.JsonDocument.Parse(rawBody);
        var root       = doc.RootElement;
        var eventType  = root.GetProperty("type").GetString();

        if (eventType != "payment.completed" && eventType != "order.fulfillment.updated")
            return;

        var referenceId = root
            .GetProperty("data").GetProperty("object")
            .GetProperty("order").GetProperty("reference_id")
            .GetString();

        if (referenceId?.StartsWith("RES-") != true) return;
        if (!int.TryParse(referenceId[4..], out var reservationId)) return;

        var updated = await _repo.UpdateReservationStatusAsync(reservationId, "Completed");
        if (updated is null) return;

        await _repo.LogNotificationAsync(
            updated.CustomerId, reservationId, "PaymentReceived", true);

        _log.LogInformation(
            "Reservation {Id} marked Completed via Square webhook.", reservationId);
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>Timer-triggered: expire stale reservations hourly.</summary>
public sealed class ExpireReservationsHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<ExpireReservationsHandler> _log;

    public ExpireReservationsHandler(
        ICustomerRepository repo,
        ILogger<ExpireReservationsHandler> log)
    {
        _repo = repo;
        _log  = log;
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

/// <summary>Get + send messages between customer and Noemi.</summary>
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
}