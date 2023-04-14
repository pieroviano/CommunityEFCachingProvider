IF NOT DEFINED Configuration SET Configuration=Release
MSBuild.exe EFProviderWrappers.sln -t:clean
MSBuild.exe EFProviderWrappers.sln -t:restore -p:RestorePackagesConfig=true
MSBuild.exe EFProviderWrappers.sln -m /property:Configuration=%Configuration%
git add -A
git commit -a --allow-empty-message -m ''
git push