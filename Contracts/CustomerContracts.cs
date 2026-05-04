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

// Identity fields (ClerkUserId, Email, IsEmailVerified) deliberately omitted —
// the controller reads them from the validated JWT and passes them to the
// handler, not the request body. Only fields the user can legitimately edit
// remain here.
public record UpsertCustomerRequest(
    string? FirstName,
    string? LastName,
    string? Phone
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

public record CreateReservationRequest(
    int     InventoryId,
    string? CustomerNotes
);

public record CancelReservationRequest(
    string? Reason
);

// ── Admin: Conversations ──────────────────────────────────────
//
// A "conversation" is one row per customer in the admin inbox view —
// the customer record plus aggregates over their messages (last message,
// unread inbound count, last activity timestamp). The thread itself is
// just the existing MessageDto list.

public record ConversationSummaryDto(
    int      CustomerId,
    string   Email,
    string?  FirstName,
    string?  LastName,
    string?  LastMessageBody,        // truncated server-side
    string?  LastMessageDirection,   // Inbound | Outbound
    DateTime? LastMessageAt,
    int      UnreadInboundCount,
    int      TotalMessages
);

public record AdminSendMessageRequest(
    string Body,
    int?   ReservationId,
    int?   OrderId
);

public record UnreadCountDto(int Count);

// ── Square ────────────────────────────────────────────────────

public record SquarePaymentLinkResult(
    string  PaymentLinkId,
    string  Url,
    string  OrderId
);