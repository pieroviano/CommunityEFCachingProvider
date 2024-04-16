using System;
using System.Data.Objects;
using EFCachingProvider.Caching;
using EFProviderWrapperToolkit;

namespace EFCachingProvider
{
    public class CachingHelper<TEntities> where TEntities:ObjectContext
    {

        private ObjectContext dbContext;

        private EFCachingConnection CachingConnection
        {
            get { return dbContext.UnwrapConnection<EFCachingConnection>(); }
        }

        public ICache Cache
        {
            get { return CachingConnection.Cache; }
            set { CachingConnection.Cache = value; }
        }

        public CachingPolicy CachingPolicy
        {
            get { return CachingConnection.CachingPolicy; }
            set { CachingConnection.CachingPolicy = value; }
        }

        public TEntities CreateDbContextForCaching(string connectionString)
        {
            var func = new Func<string, TEntities>(c=>(TEntities) Activator.CreateInstance(typeof(TEntities),
                EntityConnectionWrapperUtils.CreateEntityConnectionWithWrappersFromName(
                    c,
                    "EFCachingProvider"
                )));
            return CreateDbContextForCaching(connectionString, func);
        }

        public TEntities CreateDbContextForCaching(string connectionString, Func<string, TEntities> func)
        {
            dbContext = func?.Invoke(connectionString);
            return (TEntities) dbContext;
        }
    }
}