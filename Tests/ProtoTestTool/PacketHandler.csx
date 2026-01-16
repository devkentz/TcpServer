using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using ProtoTestTool.ScriptContract;

public class MyScriptContext : IScriptContext
{
    public IPacketSerializer Serializer { get; private set; }
    public IPacketRegistry Registry { get; private set; }
    
    private Action<string> _logger;

    public void SetLogger(Action<string> logAction)
    {
        _logger = logAction;
    }

    public void Initialize(IPacketRegistry registry, IPacketSerializer serializer)
    {
        if (_logger == null) 
        _logger = Console.WriteLine;
        
        // ---------------------------------------------------------
        // TODO: Update Path to your actual Protocol DLLs
        // ---------------------------------------------------------
        var dllPaths = new[]
        {
             @"..\..\Protocol\bin\Debug\net9.0\Protocol.dll",
             @"..\..\NetworkServer.ProtoGenerator\bin\Debug\net9.0\NetworkServer.ProtoGenerator.dll"
        };

        foreach (var path in dllPaths)
        {
            if (File.Exists(path))
            {
                LoadAssembly(path, registry);
            }
            else
            {
                _logger($"[Script] DLL not found: {path}");
            }
        }

        Registry = registry;
        Serializer = serializer;
        
        _logger("[Script] Initialized successfully.");
    }

    private void LoadAssembly(string path, IPacketRegistry registry)
    {
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(path));
            var types = assembly.GetTypes()
                .Where(t => typeof(IMessage).IsAssignableFrom(t) && !t.IsAbstract);

            int count = 0;
            foreach (var type in types)
            {
                // Simple ID logic (Hash). Replace with actual ID logic.
                int id = type.Name.GetHashCode(); 
                registry.Register(id, type);
                count++;
            }
            _logger($"[Script] Loaded {count} messages from {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            _logger($"[Script] Failed to load {path}: {ex.Message}");
        }
    }
}

return new MyScriptContext();