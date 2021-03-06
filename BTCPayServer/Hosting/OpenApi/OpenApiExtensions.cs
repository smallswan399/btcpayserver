using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using NJsonSchema;
using NJsonSchema.Generation.TypeMappers;
using NSwag;
using NSwag.Generation.Processors.Security;
using Org.BouncyCastle.Asn1.Ocsp;

namespace BTCPayServer.Hosting.OpenApi
{
    public static class OpenApiExtensions
    {
        public static IServiceCollection AddBTCPayOpenApi(this IServiceCollection serviceCollection)
        {
            return serviceCollection.AddOpenApiDocument(config =>
            {
                config.PostProcess = document =>
                {
                    document.Info.Version = "v1";
                    document.Info.Title = "BTCPay Greenfield API";
                    document.Info.Description = "A full API to use your BTCPay Server";
                    document.Info.TermsOfService = null;
                    document.Info.Contact = new NSwag.OpenApiContact
                    {
                        Name = "BTCPay Server", Email = string.Empty, Url = "https://btcpayserver.org"
                    };
                };
                config.AddOperationFilter(context =>
                {
                    var methodInfo = context.MethodInfo;
                    if (methodInfo != null)
                    {
                        return methodInfo.CustomAttributes.Any(data =>
                                   data.AttributeType == typeof(IncludeInOpenApiDocs)) ||
                               methodInfo.DeclaringType.CustomAttributes.Any(data =>
                                   data.AttributeType == typeof(IncludeInOpenApiDocs));
                    }

                    return false;
                });

                config.AddSecurity("APIKey", Enumerable.Empty<string>(),
                    new OpenApiSecurityScheme
                    {
                        Type = OpenApiSecuritySchemeType.ApiKey,
                        Name = "Authorization",
                        In = OpenApiSecurityApiKeyLocation.Header,
                        Description =
                            "BTCPay Server supports authenticating and authorizing users through an API Key that is generated by them. Send the API Key as a header value to Authorization with the format: token {token}. For a smoother experience, you can generate a url that redirects users to an API key creation screen."
                    });

                config.OperationProcessors.Add(
                    new BTCPayPolicyOperationProcessor("APIKey", AuthenticationSchemes.ApiKey));

                config.TypeMappers.Add(
                    new PrimitiveTypeMapper(typeof(PaymentType), s => s.Type = JsonObjectType.String));
                config.TypeMappers.Add(new PrimitiveTypeMapper(typeof(PaymentMethodId),
                    s => s.Type = JsonObjectType.String));
            });
        }

        public static IApplicationBuilder UseBTCPayOpenApi(this IApplicationBuilder builder)
        {
            var roothPath = builder.ApplicationServices.GetService<BTCPayServerOptions>().RootPath;
            var matched = new PathString($"{roothPath}docs");
            return builder.UseOpenApi()
                .Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments(matched, StringComparison.InvariantCultureIgnoreCase) && !context.User.Claims.Any())
                    {
                        context.Response.Redirect(  $"{context.Request.GetRelativePath(roothPath)}account/login?returnUrl={context.Request.Path}");
                        return;
                    }

                    await next.Invoke();
                })
                .UseReDoc(settings =>
                {
                    settings.Path = "/docs";
                });
        }


        class BTCPayPolicyOperationProcessor : AspNetCoreOperationSecurityScopeProcessor
        {
            private readonly string _authScheme;

            public BTCPayPolicyOperationProcessor(string x, string authScheme) : base(x)
            {
                _authScheme = authScheme;
            }

            protected override IEnumerable<string> GetScopes(IEnumerable<AuthorizeAttribute> authorizeAttributes)
            {
                var result = authorizeAttributes
                    .Where(attribute => attribute?.AuthenticationSchemes != null && attribute.Policy != null &&
                                        attribute.AuthenticationSchemes.Equals(_authScheme,
                                            StringComparison.InvariantCultureIgnoreCase))
                    .Select(attribute => attribute.Policy);

                return result;
            }
        }
    }
}
