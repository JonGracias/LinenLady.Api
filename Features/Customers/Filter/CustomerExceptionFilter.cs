namespace LinenLady.API.Api.Filters;

using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

/// <summary>
/// Maps the typed exceptions thrown by Customer/Reservation handlers onto the
/// same HTTP status codes the original Azure Functions used. Applied globally
/// via MVC options in Program.cs.
/// </summary>
public sealed class CustomerExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, message) = context.Exception switch
        {
            CustomerNotFoundException ex     => (404, ex.Message),
            ReservationNotFoundException ex  => (404, ex.Message),
            ItemNotFoundException ex         => (404, ex.Message),
            ReservationConflictException ex  => (409, ex.Message),
            ItemAlreadyReservedException ex  => (409, ex.Message),
            EmailNotVerifiedException ex     => (403, ex.Message),
            ArgumentException ex             => (400, ex.Message),
            _ => (0, "")
        };

        if (status == 0) return; // not our concern — let the framework handle it

        context.Result = new ContentResult
        {
            StatusCode = status,
            Content = message,
            ContentType = "text/plain"
        };
        context.ExceptionHandled = true;
    }
}
