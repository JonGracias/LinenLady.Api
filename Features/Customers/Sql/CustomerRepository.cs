namespace LinenLady.API.Customer.Sql;

using System.Data;
using Dapper;
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;

public interface ICustomerRepository
{
    Task<CustomerDto?>          GetByClerkIdAsync(string clerkUserId);
    Task<CustomerDto>           UpsertAsync(UpsertCustomerRequest req);
    Task<CustomerDto?>          UpdateAsync(int customerId, UpdateCustomerRequest req);

    Task<List<CustomerAddressDto>> GetAddressesAsync(int customerId);
    Task<CustomerAddressDto>       UpsertAddressAsync(int customerId, UpsertAddressRequest req, int? addressId = null);
    Task<bool>                     DeleteAddressAsync(int customerId, int addressId);

    Task<List<CustomerPreferenceDto>> GetPreferencesAsync(int customerId);
    Task                              SetPreferencesAsync(int customerId, List<string> categories);

    Task<ReservationDto?>          GetReservationAsync(int reservationId);
    Task<List<ReservationDto>>     GetCustomerReservationsAsync(int customerId);
    Task<bool>                     IsItemReservedAsync(int inventoryId);
    Task<int?>                     GetAvailableItemPriceCentsAsync(int inventoryId); // NEW
    Task<ReservationDto>           CreateReservationAsync(int customerId, CreateReservationRequest req, int amountCents);
    Task<ReservationDto?>          UpdateReservationStatusAsync(int reservationId, string status);
    Task<ReservationDto?>          SetPaymentLinkAsync(int reservationId, SquarePaymentLinkResult link);
    Task<int>                      ExpireReservationsAsync();

    Task<List<MessageDto>>  GetMessagesAsync(int customerId, int? reservationId = null);
    Task<MessageDto>        SendMessageAsync(int customerId, SendMessageRequest req, string direction = "Inbound");
    Task                    MarkMessagesReadAsync(int customerId);

    Task LogNotificationAsync(int customerId, int? reservationId, string type, bool success, string? error = null);
}

public class CustomerRepository : ICustomerRepository
{
    private readonly string _connectionString;

    public CustomerRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("Sql")
            ?? throw new InvalidOperationException("Missing connection string 'Sql'.");
    }

    private IDbConnection Connect() => new SqlConnection(_connectionString);

    // ── Customer ──────────────────────────────────────────────

    public async Task<CustomerDto?> GetByClerkIdAsync(string clerkUserId)
    {
        using var db = Connect();
        return await db.QueryFirstOrDefaultAsync<CustomerDto>(
            """
            SELECT CustomerId, ClerkUserId, Email, FirstName, LastName,
                   Phone, IsEmailVerified, CreatedAt
            FROM cust.Customer
            WHERE ClerkUserId = @ClerkUserId AND IsActive = 1
            """,
            new { ClerkUserId = clerkUserId });
    }

    public async Task<CustomerDto> UpsertAsync(UpsertCustomerRequest req)
    {
        using var db = Connect();
        return await db.QueryFirstAsync<CustomerDto>(
            """
            MERGE cust.Customer AS target
            USING (SELECT @ClerkUserId AS ClerkUserId) AS source
                ON target.ClerkUserId = source.ClerkUserId
            WHEN MATCHED THEN
                UPDATE SET
                    Email           = @Email,
                    FirstName       = @FirstName,
                    LastName        = @LastName,
                    Phone           = @Phone,
                    IsEmailVerified = @IsEmailVerified,
                    UpdatedAt       = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (ClerkUserId, Email, FirstName, LastName, Phone, IsEmailVerified)
                VALUES (@ClerkUserId, @Email, @FirstName, @LastName, @Phone, @IsEmailVerified)
            OUTPUT
                inserted.CustomerId, inserted.ClerkUserId, inserted.Email,
                inserted.FirstName,  inserted.LastName,    inserted.Phone,
                inserted.IsEmailVerified, inserted.CreatedAt;
            """,
            req);
    }

    public async Task<CustomerDto?> UpdateAsync(int customerId, UpdateCustomerRequest req)
    {
        using var db = Connect();
        return await db.QueryFirstOrDefaultAsync<CustomerDto>(
            """
            UPDATE cust.Customer
            SET FirstName = COALESCE(@FirstName, FirstName),
                LastName  = COALESCE(@LastName,  LastName),
                Phone     = COALESCE(@Phone,     Phone),
                UpdatedAt = SYSUTCDATETIME()
            OUTPUT
                inserted.CustomerId, inserted.ClerkUserId, inserted.Email,
                inserted.FirstName,  inserted.LastName,    inserted.Phone,
                inserted.IsEmailVerified, inserted.CreatedAt
            WHERE CustomerId = @CustomerId
            """,
            new { CustomerId = customerId, req.FirstName, req.LastName, req.Phone });
    }

    // ── Address ───────────────────────────────────────────────

    public async Task<List<CustomerAddressDto>> GetAddressesAsync(int customerId)
    {
        using var db = Connect();
        var rows = await db.QueryAsync<CustomerAddressDto>(
            """
            SELECT AddressId, CustomerId, Label, Street1, Street2,
                   City, State, Zip, Country, IsDefault
            FROM cust.CustomerAddress
            WHERE CustomerId = @CustomerId
            ORDER BY IsDefault DESC, CreatedAt ASC
            """,
            new { CustomerId = customerId });
        return rows.ToList();
    }

    public async Task<CustomerAddressDto> UpsertAddressAsync(
        int customerId, UpsertAddressRequest req, int? addressId = null)
    {
        using var db = Connect();

        if (req.IsDefault)
        {
            await db.ExecuteAsync(
                "UPDATE cust.CustomerAddress SET IsDefault = 0 WHERE CustomerId = @CustomerId",
                new { CustomerId = customerId });
        }

        if (addressId.HasValue)
        {
            return await db.QueryFirstAsync<CustomerAddressDto>(
                """
                UPDATE cust.CustomerAddress
                SET Label     = @Label,
                    Street1   = @Street1,
                    Street2   = @Street2,
                    City      = @City,
                    State     = @State,
                    Zip       = @Zip,
                    Country   = @Country,
                    IsDefault = @IsDefault,
                    UpdatedAt = SYSUTCDATETIME()
                OUTPUT inserted.AddressId, inserted.CustomerId, inserted.Label,
                       inserted.Street1,   inserted.Street2,    inserted.City,
                       inserted.State,     inserted.Zip,        inserted.Country,
                       inserted.IsDefault
                WHERE AddressId = @AddressId AND CustomerId = @CustomerId
                """,
                new { AddressId = addressId, CustomerId = customerId,
                      req.Label, req.Street1, req.Street2, req.City,
                      req.State, req.Zip, req.Country, req.IsDefault });
        }

        return await db.QueryFirstAsync<CustomerAddressDto>(
            """
            INSERT INTO cust.CustomerAddress
                (CustomerId, Label, Street1, Street2, City, State, Zip, Country, IsDefault)
            OUTPUT inserted.AddressId, inserted.CustomerId, inserted.Label,
                   inserted.Street1,   inserted.Street2,    inserted.City,
                   inserted.State,     inserted.Zip,        inserted.Country,
                   inserted.IsDefault
            VALUES
                (@CustomerId, @Label, @Street1, @Street2, @City, @State, @Zip, @Country, @IsDefault)
            """,
            new { CustomerId = customerId, req.Label, req.Street1, req.Street2,
                  req.City, req.State, req.Zip, req.Country, req.IsDefault });
    }

    public async Task<bool> DeleteAddressAsync(int customerId, int addressId)
    {
        using var db = Connect();
        var rows = await db.ExecuteAsync(
            "DELETE FROM cust.CustomerAddress WHERE AddressId = @AddressId AND CustomerId = @CustomerId",
            new { AddressId = addressId, CustomerId = customerId });
        return rows > 0;
    }

    // ── Preferences ───────────────────────────────────────────

    public async Task<List<CustomerPreferenceDto>> GetPreferencesAsync(int customerId)
    {
        using var db = Connect();
        var rows = await db.QueryAsync<CustomerPreferenceDto>(
            """
            SELECT PreferenceId, CustomerId, Category, NotifyOnNew
            FROM cust.CustomerPreference
            WHERE CustomerId = @CustomerId
            """,
            new { CustomerId = customerId });
        return rows.ToList();
    }

    public async Task SetPreferencesAsync(int customerId, List<string> categories)
    {
        using var db = Connect();
        db.Open();
        using var tx = db.BeginTransaction();
        try
        {
            await db.ExecuteAsync(
                "DELETE FROM cust.CustomerPreference WHERE CustomerId = @CustomerId",
                new { CustomerId = customerId }, tx);

            foreach (var cat in categories.Distinct())
            {
                await db.ExecuteAsync(
                    """
                    INSERT INTO cust.CustomerPreference (CustomerId, Category, NotifyOnNew)
                    VALUES (@CustomerId, @Category, 1)
                    """,
                    new { CustomerId = customerId, Category = cat }, tx);
            }
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    // ── Reservation ───────────────────────────────────────────

    private const string ReservationSelect = """
        SELECT
            r.ReservationId, r.CustomerId, r.InventoryId, r.Status,
            r.ReservedAt, r.ExpiresAt, r.PaymentSentAt, r.CompletedAt,
            r.CustomerNotes, r.SquarePaymentLinkUrl, r.AmountCents,
            i.Name      AS ItemName,
            i.Sku       AS ItemSku,
            i.PublicId  AS ItemPublicId,
            NULL        AS ThumbnailUrl
        FROM cust.Reservation r
        JOIN inv.Inventory    i ON i.InventoryId = r.InventoryId
        """;

    public async Task<ReservationDto?> GetReservationAsync(int reservationId)
    {
        using var db = Connect();
        return await db.QueryFirstOrDefaultAsync<ReservationDto>(
            ReservationSelect + " WHERE r.ReservationId = @ReservationId",
            new { ReservationId = reservationId });
    }

    public async Task<List<ReservationDto>> GetCustomerReservationsAsync(int customerId)
    {
        using var db = Connect();
        var rows = await db.QueryAsync<ReservationDto>(
            ReservationSelect + """
             WHERE r.CustomerId = @CustomerId
             ORDER BY r.ReservedAt DESC
            """,
            new { CustomerId = customerId });
        return rows.ToList();
    }

    public async Task<bool> IsItemReservedAsync(int inventoryId)
    {
        using var db = Connect();
        var count = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM cust.Reservation
            WHERE InventoryId = @InventoryId
              AND Status IN ('Pending','Confirmed','PaymentSent')
              AND ExpiresAt > SYSUTCDATETIME()
            """,
            new { InventoryId = inventoryId });
        return count > 0;
    }

    public async Task<int?> GetAvailableItemPriceCentsAsync(int inventoryId)
    {
        using var db = Connect();
        return await db.ExecuteScalarAsync<int?>(
            """
            SELECT UnitPriceCents FROM inv.Inventory
            WHERE InventoryId = @Id AND IsActive = 1 AND IsDeleted = 0
            """,
            new { Id = inventoryId });
    }

    public async Task<ReservationDto> CreateReservationAsync(
        int customerId, CreateReservationRequest req, int amountCents)
    {
        using var db = Connect();
        return await db.QueryFirstAsync<ReservationDto>(
            """
            INSERT INTO cust.Reservation
                (CustomerId, InventoryId, Status, ExpiresAt, CustomerNotes, AmountCents)
            OUTPUT
                inserted.ReservationId, inserted.CustomerId, inserted.InventoryId,
                inserted.Status, inserted.ReservedAt, inserted.ExpiresAt,
                inserted.PaymentSentAt, inserted.CompletedAt, inserted.CustomerNotes,
                inserted.SquarePaymentLinkUrl, inserted.AmountCents,
                NULL AS ItemName, NULL AS ItemSku, NULL AS ItemPublicId, NULL AS ThumbnailUrl
            VALUES
                (@CustomerId, @InventoryId, 'Pending',
                 DATEADD(HOUR, 48, SYSUTCDATETIME()),
                 @CustomerNotes, @AmountCents)
            """,
            new { CustomerId = customerId, req.InventoryId,
                  req.CustomerNotes, AmountCents = amountCents });
    }

    public async Task<ReservationDto?> UpdateReservationStatusAsync(
        int reservationId, string status)
    {
        using var db = Connect();
        var setClause = status switch
        {
            "Completed"   => ", CompletedAt   = SYSUTCDATETIME()",
            "PaymentSent" => ", PaymentSentAt = SYSUTCDATETIME()",
            "Cancelled"   => ", CancelledAt   = SYSUTCDATETIME()",
            _             => ""
        };

        return await db.QueryFirstOrDefaultAsync<ReservationDto>(
            $"""
            UPDATE cust.Reservation
            SET Status    = @Status,
                UpdatedAt = SYSUTCDATETIME()
                {setClause}
            OUTPUT
                inserted.ReservationId, inserted.CustomerId, inserted.InventoryId,
                inserted.Status, inserted.ReservedAt, inserted.ExpiresAt,
                inserted.PaymentSentAt, inserted.CompletedAt, inserted.CustomerNotes,
                inserted.SquarePaymentLinkUrl, inserted.AmountCents,
                NULL AS ItemName, NULL AS ItemSku, NULL AS ItemPublicId, NULL AS ThumbnailUrl
            WHERE ReservationId = @ReservationId
            """,
            new { ReservationId = reservationId, Status = status });
    }

    public async Task<ReservationDto?> SetPaymentLinkAsync(
        int reservationId, SquarePaymentLinkResult link)
    {
        using var db = Connect();
        return await db.QueryFirstOrDefaultAsync<ReservationDto>(
            """
            UPDATE cust.Reservation
            SET SquarePaymentLinkId  = @PaymentLinkId,
                SquarePaymentLinkUrl = @Url,
                SquareOrderId        = @OrderId,
                Status               = 'PaymentSent',
                PaymentSentAt        = SYSUTCDATETIME(),
                UpdatedAt            = SYSUTCDATETIME()
            OUTPUT
                inserted.ReservationId, inserted.CustomerId, inserted.InventoryId,
                inserted.Status, inserted.ReservedAt, inserted.ExpiresAt,
                inserted.PaymentSentAt, inserted.CompletedAt, inserted.CustomerNotes,
                inserted.SquarePaymentLinkUrl, inserted.AmountCents,
                NULL AS ItemName, NULL AS ItemSku, NULL AS ItemPublicId, NULL AS ThumbnailUrl
            WHERE ReservationId = @ReservationId
            """,
            new { ReservationId = reservationId,
                  link.PaymentLinkId, link.Url, link.OrderId });
    }

    public async Task<int> ExpireReservationsAsync()
    {
        using var db = Connect();
        return await db.ExecuteAsync(
            """
            UPDATE cust.Reservation
            SET Status    = 'Expired',
                UpdatedAt = SYSUTCDATETIME()
            WHERE Status  IN ('Pending','Confirmed')
              AND ExpiresAt < SYSUTCDATETIME()
            """);
    }

    // ── Messages ──────────────────────────────────────────────

    public async Task<List<MessageDto>> GetMessagesAsync(
        int customerId, int? reservationId = null)
    {
        using var db = Connect();
        var sql = reservationId.HasValue
            ? """
              SELECT MessageId, CustomerId, ReservationId, Direction, Body, IsRead, SentAt
              FROM cust.Message
              WHERE CustomerId = @CustomerId AND ReservationId = @ReservationId
              ORDER BY SentAt ASC
              """
            : """
              SELECT MessageId, CustomerId, ReservationId, Direction, Body, IsRead, SentAt
              FROM cust.Message
              WHERE CustomerId = @CustomerId
              ORDER BY SentAt ASC
              """;

        var rows = await db.QueryAsync<MessageDto>(
            sql, new { CustomerId = customerId, ReservationId = reservationId });
        return rows.ToList();
    }

    public async Task<MessageDto> SendMessageAsync(
        int customerId, SendMessageRequest req, string direction = "Inbound")
    {
        using var db = Connect();
        return await db.QueryFirstAsync<MessageDto>(
            """
            INSERT INTO cust.Message (CustomerId, ReservationId, Direction, Body)
            OUTPUT inserted.MessageId, inserted.CustomerId, inserted.ReservationId,
                   inserted.Direction, inserted.Body, inserted.IsRead, inserted.SentAt
            VALUES (@CustomerId, @ReservationId, @Direction, @Body)
            """,
            new { CustomerId = customerId, req.ReservationId,
                  Direction = direction, req.Body });
    }

    public async Task MarkMessagesReadAsync(int customerId)
    {
        using var db = Connect();
        await db.ExecuteAsync(
            """
            UPDATE cust.Message SET IsRead = 1
            WHERE CustomerId = @CustomerId AND Direction = 'Outbound' AND IsRead = 0
            """,
            new { CustomerId = customerId });
    }

    // ── Notifications ─────────────────────────────────────────

    public async Task LogNotificationAsync(
        int customerId, int? reservationId, string type,
        bool success, string? error = null)
    {
        using var db = Connect();
        await db.ExecuteAsync(
            """
            INSERT INTO cust.Notification
                (CustomerId, ReservationId, Type, Success, ErrorMessage)
            VALUES
                (@CustomerId, @ReservationId, @Type, @Success, @ErrorMessage)
            """,
            new { CustomerId = customerId, ReservationId = reservationId,
                  Type = type, Success = success, ErrorMessage = error });
    }
}
