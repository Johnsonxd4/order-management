using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace SharedKernel;

public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            return await next(context);
        }

        var validationContext = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, validationContext, validationResults, validateAllProperties: true);

        if (!isValid)
        {
            return Results.ValidationProblem(ServiceCollectionExtensions.ToValidationDictionary(validationResults));
        }

        return await next(context);
    }
}
