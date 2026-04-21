namespace LinenLady.API.Contracts;

// ── Customer ──────────────────────────────────────────────────

public record CustomerDto(
    int      CustomerId,
    string   ClerkUserId,
    string   Email,
    string?  FirstName,
    string?  LastName,
    string?  Phone,
    bool     IsEmailVerified,
    DateTime CreatedAt
);

public record UpsertCustomerRequest(
    string  ClerkUserId,
    string  Email,
    string? FirstName,
    string? LastName,
    string? Phone,
    bool    IsEmailVerified
);

public record UpdateCustomerRequest(
    string? FirstName,
    string? LastName,
    string? Phone
);

// ── Address ───────────────────────────────────────────────────

public record CustomerAddressDto(
    int     AddressId,
    int     CustomerId,
    string  Label,
    string  Street1,
    string? Street2,
    string  City,
    string  State,
    string  Zip,
    string  Country,
    bool    IsDefault
);

public record UpsertAddressRequest(
    string  Label,
    string  Street1,
    string? Street2,
    string  City,
    string  State,
    string  Zip,
    string  Country  = "US",
    bool    IsDefault = false
);

// ── Preferences ───────────────────────────────────────────────

public record CustomerPreferenceDto(
    int    PreferenceId,
    int    CustomerId,
    string Category,
    bool   NotifyOnNew
);

public record SetPreferencesRequest(
    // List of categories the customer wants new-arrival alerts for
    List<string> Categories
);

// ── Reservation ───────────────────────────────────────────────

public record ReservationDto(
    int      ReservationId,
    int      CustomerId,
    int      InventoryId,
    string   Status,
    DateTime ReservedAt,
    DateTime ExpiresAt,
    DateTime? PaymentSentAt,
    DateTime? CompletedAt,
    string?  CustomerNotes,
    string?  SquarePaymentLinkUrl,
    int      AmountCents,
    // Denormalized for convenience
    string?  ItemName,
    string?  ItemSku,
    string?  ItemPublicId,
    string?  ThumbnailUrl
);

public record CreateReservationRequest(
    int     InventoryId,
    string? CustomerNotes
);

public record CancelReservationRequest(
    string? Reason
);

// ── Message ───────────────────────────────────────────────────

public record MessageDto(
    int      MessageId,
    int      CustomerId,
    int?     ReservationId,
    string   Direction,   // Inbound | Outbound
    string   Body,
    bool     IsRead,
    DateTime SentAt
);

public record SendMessageRequest(
    string Body,
    int?   ReservationId
);

// ── Square ────────────────────────────────────────────────────

public record SquarePaymentLinkResult(
    string  PaymentLinkId,
    string  Url,
    string  OrderId
);