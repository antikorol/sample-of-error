using Microsoft.AspNetCore.Http;
using Ocelot.Configuration;
using Ocelot.LoadBalancer.LoadBalancers;
using Ocelot.Responses;
using Ocelot.ServiceDiscovery.Providers;
using Ocelot.Values;

namespace ApiGateway.Service;

public class RoundRobinCreator2: ILoadBalancerCreator
{
    public Response<ILoadBalancer> Create(DownstreamRoute route, IServiceDiscoveryProvider serviceProvider)
    {
        return new OkResponse<ILoadBalancer>(new RoundRobin2(async () => await serviceProvider.GetAsync()));
    }

    public string Type => nameof(RoundRobin2);
}

public class RoundRobin2 : ILoadBalancer
{
    private readonly Func<Task<List<Ocelot.Values.Service>>> _servicesDelegate;
    private readonly object _lock = new();

    private int _last;

    public RoundRobin2(Func<Task<List<Ocelot.Values.Service>>> services)
    {
        _servicesDelegate = services;
    }

    public async Task<Response<ServiceHostAndPort>> Lease(HttpContext httpContext)
    {
        var services = await _servicesDelegate?.Invoke() ?? new List<Ocelot.Values.Service>();

        if (services?.Count != 0)
        {
            lock (_lock)
            {
                if (_last >= services.Count)
                {
                    _last = 0;
                }

                var next = services[_last++];
                return new OkResponse<ServiceHostAndPort>(next.HostAndPort);
            }
        }

        return new ErrorResponse<ServiceHostAndPort>(new ServicesAreEmptyError($"There were no services in {nameof(RoundRobin)} during {nameof(Lease)} operation."));
    }

    public void Release(ServiceHostAndPort hostAndPort)
    {
    }
}