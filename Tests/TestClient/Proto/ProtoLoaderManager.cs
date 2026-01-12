using System.Reflection;


namespace TestClient.Proto
{
	public class ProtoLoaderManager
	{
		public readonly Dictionary<string, PacketConvertor> PacketsByMsgId = new();
		public readonly List<PacketConvertor> SendPackets = new();
		private static readonly Lazy<ProtoLoaderManager> SInstance = new Lazy<ProtoLoaderManager>(() => new ProtoLoaderManager());

		public static ProtoLoaderManager Instance => SInstance.Value;
		private ProtoLoaderManager() { }

		public void LoadAllProtos()
		{
			var protoAssembly = Assembly.Load("Nb.Protocol");
			var @namespace = "Proto";

			var messages = AppDomain.CurrentDomain
				.GetAssemblies()
				.Concat(new[] { protoAssembly })
				.SelectMany(assembly => assembly.GetTypes().Where(e => e.Namespace == @namespace)).Distinct().ToList();

			var sendPackets = messages.Where(e => e.FullName != null && e.FullName.EndsWith("Req")).ToList();
			var receivePackets = messages.Where(e =>
				e.FullName != null && (e.FullName.EndsWith("Res") || e.FullName.EndsWith("Notify") || e.FullName.EndsWith("NotifyMsg"))).ToList();

			foreach (var type in sendPackets)
			{
				var convertor = new PacketConvertor { Name = type.Name, Type = type };
				PacketsByMsgId.Add(type.Name, convertor);
				SendPackets.Add(convertor);
			}

			foreach (var type in receivePackets)
				PacketsByMsgId.Add(type.Name, new PacketConvertor { Name = type.Name, Type = type });
		}

		public PacketConvertor? Find(string name) => PacketsByMsgId.GetValueOrDefault(name);

		public IReadOnlyList<PacketConvertor> GetSendPackets() => SendPackets;
	}
}
