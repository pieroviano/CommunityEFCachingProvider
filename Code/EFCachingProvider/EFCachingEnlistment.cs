using System;
using System.Collections.Generic;
using System.Data.Metadata.Edm;
using System.Linq;
using System.Text;
using System.Transactions;
using EFCachingProvider.Caching;

namespace EFCachingProvider
{
	internal class EFCachingEnlistment : IEnlistmentNotification
	{
		private HashSet<EntitySetBase> affectedEntitySets = new HashSet<EntitySetBase>();

		public EFCachingEnlistment()
		{
			HasModifications = false;
		}

		public void Commit(Enlistment enlistment)
		{
			if (Cache != null && this.HasModifications)
			{
				Cache.InvalidateSets(this.affectedEntitySets.Select(c => c.Name));
			}

			enlistment.Done();
		}

		public void InDoubt(Enlistment enlistment)
		{
			enlistment.Done();
		}

		public void Prepare(PreparingEnlistment preparingEnlistment)
		{
			preparingEnlistment.Prepared();
		}

		public void Rollback(Enlistment enlistment)
		{
			enlistment.Done();
		}

		internal void AddAffectedEntitySet(EntitySetBase entitySet)
		{
			this.affectedEntitySets.Add(entitySet);
		}

		public bool HasModifications { get; set; }

		public ICache Cache { get; set; }
	}
}
