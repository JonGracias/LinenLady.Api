namespace LinenLady.API.Inventory.Availability.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Customers.Sql;
using LinenLady.API.Inventory.Sql;

public sealed class GetAvailabilityHandler
{
    private readonly IInventoryRepository _inv;
    private readonly ICustomerRepository  _customers;

    public GetAvailabilityHandler(IInventoryRepository inv, ICustomerRepository customers)
    {
        _inv = inv;
        _customers = customers;
    }

    public async Task<GetAvailabilityResponse> Handle(
        GetAvailabilityRequest req,
        string? clerkUserId,
        CancellationToken ct)
    {
        var ids = (req.InventoryIds ?? new())
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var rows = await _inv.GetAvailability(ids, ct);

        if (rows.Count == 0)
            return new GetAvailabilityResponse();

        if (string.IsNullOrWhiteSpace(clerkUserId))
            return new GetAvailabilityResponse { Items = rows };

        var customer = await _customers.GetByClerkIdAsync(clerkUserId);
        if (customer is null)
            return new GetAvailabilityResponse { Items = rows };

        var personalized = rows
            .Select(r => r.State switch
            {
                "InBasket"       when r.BlockingCustomerId == customer.CustomerId => r with { State = "YourBasket" },
                "PendingPayment" when r.BlockingCustomerId == customer.CustomerId => r with { State = "YourPendingPayment" },
                _ => r
            })
            .ToList();

        return new GetAvailabilityResponse { Items = personalized };
    }
}