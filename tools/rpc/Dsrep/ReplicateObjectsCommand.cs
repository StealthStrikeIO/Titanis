using System.ComponentModel;
using System.Net;
using Titanis.Ldap;
using Titanis.Msrpc.Msdrsr;
using Titanis.Net;

namespace Titanis.Cli.Dsrep;

[Command]
[Description("Requests replica changes")]
public class ReplicateObjectsCommand : ReplicateCommand
{
	[Parameter(After = nameof(RpcCommand.ServerName))]
	[Description("DN, GUID, or SID of object to retrieve")]
	public DsobjSpec[]? ObjectName { get; set; }

	[Parameter]
	[DefaultValue(1)]
	[Description("Number of parallel requests")]
	public int Parallelize { get; set; }

	protected override int GetDegreeOfParallelism() => this.Parallelize;

	private bool UseNcReplication => this.SecretsDump.IsSet && this.ObjectName.IsNullOrEmpty();

	protected sealed override ExtendedOpRequest GetExop() =>
		UseNcReplication ? 0 : ExtendedOpRequest.ReplObject;

	protected override async IAsyncEnumerable<DsName> GetObjectNames(CancellationToken cancellationToken)
	{
		if (UseNcReplication)
		{
			var ldapClient = await LdapClient.Connect(new DnsEndPoint(this.CurrentServerName, 389), null, this.RequireService<ISocketService>(), this.RequireService<IClientCredentialService>(), cancellationToken);
			yield return new DsName(Guid.Empty, null, ldapClient.DomainRoot);
			yield break;
		}

		LdapClient? ldapSearch = null;
		var objSpecs = this.ObjectName;

		if (objSpecs.IsNullOrEmpty())
		{
			objSpecs = [new DsobjSpec(LdapFilter.Parse("(objectClass=*)"))];
		}

		foreach (var objSpec in objSpecs)
		{
			var objName = objSpec.Dsname;
			if (objName is not null)
			{
				yield return objName;
			}
			else
			{
				ldapSearch ??= await LdapClient.Connect(new DnsEndPoint(this.CurrentServerName, 389), null, this.RequireService<ISocketService>(), this.RequireService<IClientCredentialService>(), cancellationToken);

				var filter = objSpec.Filter ?? LdapFilter.Parse($"(samAccountName={objSpec.Name})");
				LdapQuery query = new(ldapSearch.DomainRoot, LdapSearchScope.Subtree, filter, [])
				{
					PageSize = 20,
					Options = LdapQueryOptions.AllPages
				};

				var results = await ldapSearch.Search(query, cancellationToken);
				if (results.EntryCount == 0)
				{
					this.WriteWarning($"The object specification '{objSpec?.ToString()}' did not return any results");
				}
				else
				{
					foreach (var entry in results.Entries)
					{
						if (entry.EntryName is not null)
							yield return new DsName(Guid.Empty, null, entry.EntryName);
					}
				}
			}
		}
	}
}
