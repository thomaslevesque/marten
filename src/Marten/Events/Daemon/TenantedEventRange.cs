using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon
{
    internal class TenantedEventRange: EventRangeGroup
    {
        private readonly DocumentStore _store;
        private readonly IProjection _projection;

        public TenantedEventRange(IDocumentStore store, ITenancy storeTenancy, IProjection projection, EventRange range,
            CancellationToken shardCancellation) : base(range, shardCancellation)
        {
            _store = (DocumentStore)store;
            _projection = projection;

            var byTenant = range.Events.GroupBy(x => x.TenantId);
            foreach (var group in byTenant)
            {
                var tenant = storeTenancy[group.Key];

                var actions = _store.Events.StreamIdentity switch
                {
                    StreamIdentity.AsGuid => group.GroupBy(x => x.StreamId)
                        .Select(events => StreamAction.For(events.Key, events.ToList())),

                    StreamIdentity.AsString => group.GroupBy(x => x.StreamKey)
                        .Select(events => StreamAction.For(events.Key, events.ToList())),

                    _ => null
                };

                Groups.Add(new TenantActionGroup(tenant, actions));
            }
        }

        public IList<TenantActionGroup> Groups { get; } = new List<TenantActionGroup>();

        protected override void reset()
        {
            // Nothing
        }

        public override void Dispose()
        {
            // Nothing
        }

        public override string ToString()
        {
            return $"Tenant Group Range for: {Range}";
        }

        public override Task ConfigureUpdateBatch(IShardAgent shardAgent, ProjectionUpdateBatch batch)
        {
            var tasks = Groups.Select(tenantGroup =>
            {
                return shardAgent.TryAction(async () =>
                {
                    await tenantGroup.ApplyEvents(batch, _projection, _store, Cancellation);
                }, Cancellation);
            }).ToArray();

            return Task.WhenAll(tasks);
        }
    }
}
