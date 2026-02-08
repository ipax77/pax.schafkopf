namespace sk.AppHost;

public static class ResourceBuilderExtensions
{
    public static IResourceBuilder<T> WithHttpEndpointsAsWebSockets<T>(this IResourceBuilder<T> builder) where T : IResourceWithEndpoints
    {
        if (!builder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var endpoints))
        {
            return builder;
        }

        foreach (var endpoint in endpoints.Where(x => x.UriScheme == Uri.UriSchemeHttp || x.UriScheme == Uri.UriSchemeHttps))
        {
            var websocketScheme = endpoint.UriScheme == Uri.UriSchemeHttps
                ? Uri.UriSchemeWss
                : Uri.UriSchemeWs;

            builder.WithEndpoint(
                endpoint.Port,
                endpoint.TargetPort,
                isExternal: endpoint.IsExternal,
                isProxied: false,
                protocol: endpoint.Protocol,
                name: websocketScheme,
                scheme: websocketScheme
            );
        }

        return builder;
    }
}
