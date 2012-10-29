CommunityEFCachingProvider
==========================

Fork of the EFCachingProvider sample to add in transactionscope and execute store command support

The original code is here:
[http://code.msdn.microsoft.com/EFProviderWrappers-c0b88f32](http://code.msdn.microsoft.com/EFProviderWrappers-c0b88f32)

This code changes 2 things.
First of all, it incorporates the code from this gist:
[https://gist.github.com/797390 ](https://gist.github.com/797390 )
in order to make the cache work correctly with a TransactionScope. 

Secondly, it removes the protection on ExecuteStoreCommand, which is dangerous as the cache won't get invalidated by thse queries, but as long as you understand that, it is ok to use. 