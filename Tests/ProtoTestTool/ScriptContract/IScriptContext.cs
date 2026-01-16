namespace ProtoTestTool.ScriptContract
{
    /// <summary>
    /// The main entry point that the script must return.
    /// It aggregates the serializer and registry.
    /// </summary>
    public interface IScriptContext
    {
        IPacketSerializer Serializer { get; }
        IPacketRegistry Registry { get; }

        /// <summary>
        /// Optional: Initialize logic (e.g. loading DLLs manually if needed).
        /// </summary>
        void Initialize(IPacketRegistry registry, IPacketSerializer packetSerializer);

        /// <summary>
        /// Injects a logger action from the host to the script.
        /// Use this to print debug logs from within the script.
        /// </summary>
        void SetLogger(Action<string> logAction);
    }
}