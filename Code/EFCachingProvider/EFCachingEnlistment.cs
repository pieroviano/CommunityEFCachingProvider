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

			this.IsCompleted = true;
			enlistment.Done();
		}

		public void InDoubt(Enlistment enlistment)
		{
			this.IsCompleted = true;
			enlistment.Done();
		}

		public void Prepare(PreparingEnlistment preparingEnlistment)
		{
			preparingEnlistment.Prepared();
		}

		public void Rollback(Enlistment enlistment)
		{
			this.IsCompleted = true;
			enlistment.Done();
		}

		internal void AddAffectedEntitySet(EntitySetBase entitySet)
		{
			this.affectedEntitySets.Add(entitySet);
		}

		public bool HasModifications { get; set; }

		/// <summary>Set once the transaction has committed, rolled back or gone in doubt.</summary>
		internal bool IsCompleted { get; private set; }

		public ICache Cache { get; set; }
	}
}
