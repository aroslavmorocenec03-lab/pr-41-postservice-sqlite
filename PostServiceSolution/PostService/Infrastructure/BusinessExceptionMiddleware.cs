using PostService.BusinessLogic;

namespace PostService.Infrastructure;

// HTTP-рівень лише перетворює відхилення сервісу на відповідь 400.
public class BusinessExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (PostingValidationException exception)
        {
            await Results.ValidationProblem(exception.Errors).ExecuteAsync(context);
        }
    }
}
