using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SharedExploration;

// Server-only: records where every connected player has been and adds it to the explored area
// stored in every map table. Players then get everyone's paths with the map table's "Read".
// Map tables also share their explored areas with each other, so a manual "Record" on one table
// reaches all of them.
[BepInPlugin(Guid, "SharedExploration", Version)]
public sealed class SharedExplorationPlugin : BaseUnityPlugin
{
	public const string Guid = "Tie.SharedExploration";

	// The id before 1.1.0, which named the settings file.
	private const string OldGuid = "local.sharedexploration";

	public const string Version = "1.1.1";

	// Minimap.Version.SharedMap the map table data is written in (explored bools + pins with author).
	private const int SharedMapVersion = 3;

	private const int Header = 8;

	private ConfigEntry<bool> _enabled;

	private ConfigEntry<float> _sampleInterval;

	private ConfigEntry<float> _tableInterval;

	private ConfigEntry<float> _exploreRadius;

	private int _size;

	private float _pixelSize;

	private float _gameRadius;

	private byte[] _explored;

	// Bumped whenever _explored gains a pixel; tables written at the current value are up to date.
	private int _revision;

	private int _savedRevision;

	private string _saveFile;

	private string[] _tablePrefabs;

	private readonly Dictionary<ZDOID, (uint DataRevision, int Revision)> _tables = new Dictionary<ZDOID, (uint, int)>();

	private float _sampleTimer;

	private float _tableTimer;

	private bool _updating;

	private int _tableCount;

	private int _written;

	private void Awake()
	{
		// Settings made under the old id move to the new file, so they are kept.
		string old = System.IO.Path.Combine(Paths.ConfigPath, OldGuid + ".cfg");
		if (System.IO.File.Exists(old) && !System.IO.File.Exists(Config.ConfigFilePath))
		{
			try
			{
				System.IO.File.Move(old, Config.ConfigFilePath);
				Config.Reload();
			}
			catch (System.IO.IOException)
			{
			}
		}
		_enabled = Config.Bind("General", "Enabled", true, "Add the areas players explore to every map table.");
		_sampleInterval = Config.Bind("General", "SampleIntervalSeconds", 2f, "How often player positions are recorded (the game explores every 2 s).");
		_tableInterval = Config.Bind("General", "MapTableUpdateSeconds", 30f, "How often map tables are updated with the recorded areas.");
		_exploreRadius = Config.Bind("General", "ExploreRadius", 0f, "Radius in metres revealed around each player. 0 = the game's own radius.");
		new Terminal.ConsoleCommand("mapexplore", "[update] - shared exploration status; 'update' writes map tables now; use through 'server mapexplore'", Run);
	}

	private void Update()
	{
		if (!_enabled.Value || ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null)
		{
			return;
		}
		if (_explored == null && !Init())
		{
			return;
		}
		_sampleTimer += Time.deltaTime;
		if (_sampleTimer >= Mathf.Max(0.5f, _sampleInterval.Value))
		{
			_sampleTimer = 0f;
			Sample();
		}
		_tableTimer += Time.deltaTime;
		if (_tableTimer >= Mathf.Max(5f, _tableInterval.Value) && !_updating)
		{
			_tableTimer = 0f;
			StartCoroutine(UpdateTables());
		}
	}

	private bool Init()
	{
		Minimap minimap = Minimap.instance;
		if (minimap == null)
		{
			return false;
		}
		_size = minimap.m_textureSize;
		_pixelSize = minimap.m_pixelSize;
		_gameRadius = minimap.m_exploreRadius;
		_tablePrefabs = ZNetScene.instance.m_prefabs.Where((GameObject p) => p != null && p.GetComponent<MapTable>() != null).Select((GameObject p) => p.name).ToArray();
		_explored = new byte[_size * _size];
		string world = string.Concat(ZNet.instance.GetWorldName().Split(Path.GetInvalidFileNameChars()));
		_saveFile = Path.Combine(Paths.ConfigPath, "SharedExploration", world + ".explored.gz");
		Load();
		Logger.LogInfo($"Map {_size}x{_size} px at {_pixelSize} m/px, explore radius {Radius()} m, map tables: {string.Join(", ", _tablePrefabs)}");
		return true;
	}

	private float Radius()
	{
		return _exploreRadius.Value > 0f ? _exploreRadius.Value : _gameRadius;
	}

	private void Sample()
	{
		foreach (ZNetPeer peer in ZNet.instance.GetPeers())
		{
			// No character yet means the player is still loading in, and m_refPos is not their position.
			if (!peer.m_characterID.IsNone())
			{
				Explore(peer.m_refPos, Radius());
			}
		}
	}

	// Same shape as Minimap.Explore, so the shared area matches what the player sees on their own map.
	private void Explore(Vector3 p, float radius)
	{
		int r = (int)Mathf.Ceil(radius / _pixelSize);
		int half = _size / 2;
		int px = Utils.RoundToInt(p.x / _pixelSize + half);
		int py = Utils.RoundToInt(p.z / _pixelSize + half);
		bool added = false;
		for (int y = py - r; y <= py + r; y++)
		{
			if (y < 0 || y >= _size)
			{
				continue;
			}
			for (int x = px - r; x <= px + r; x++)
			{
				if (x < 0 || x >= _size || new Vector2(x - px, y - py).magnitude > r)
				{
					continue;
				}
				int i = y * _size + x;
				if (_explored[i] == 0)
				{
					_explored[i] = 1;
					added = true;
				}
			}
		}
		if (added)
		{
			_revision++;
		}
	}

	private IEnumerator UpdateTables()
	{
		_updating = true;
		try
		{
			List<ZDO> zdos = new List<ZDO>();
			foreach (string prefab in _tablePrefabs)
			{
				int index = 0;
				while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, zdos, ref index))
				{
					yield return null;
				}
			}
			_tableCount = zdos.Count;
			_written = 0;
			// Two passes: the first also collects what tables already hold, the second hands that to the
			// tables visited before it was found.
			for (int pass = 0; pass < 2; pass++)
			{
				foreach (ZDO zdo in zdos)
				{
					if (zdo.IsValid() && UpdateTable(zdo))
					{
						yield return null;
					}
				}
			}
			foreach (ZDOID id in _tables.Keys.Where((ZDOID id) => !zdos.Any((ZDO z) => z.m_uid == id)).ToList())
			{
				_tables.Remove(id);
			}
			if (_written > 0)
			{
				Logger.LogInfo($"Updated {_written} of {zdos.Count} map table(s); {Explored():N0} explored map pixels shared.");
			}
			Save();
		}
		finally
		{
			_updating = false;
		}
	}

	// Returns true when the table had to be decompressed (so the caller can spread the work over frames).
	private bool UpdateTable(ZDO zdo)
	{
		if (_tables.TryGetValue(zdo.m_uid, out var seen) && seen.DataRevision == zdo.DataRevision && seen.Revision == _revision)
		{
			return false;
		}
		int n = _size * _size;
		byte[] stored = zdo.GetByteArray(ZDOVars.s_data);
		byte[] data;
		if (stored == null)
		{
			// A table nobody has written to: explored area and no pins.
			data = new byte[Header + n + 4];
			BitConverter.GetBytes(SharedMapVersion).CopyTo(data, 0);
			BitConverter.GetBytes(n).CopyTo(data, 4);
		}
		else
		{
			data = Utils.Decompress(stored);
			int version = data.Length >= Header ? BitConverter.ToInt32(data, 0) : -1;
			int count = data.Length >= Header ? BitConverter.ToInt32(data, 4) : -1;
			if (version != SharedMapVersion || count != n || data.Length < Header + n)
			{
				Logger.LogWarning($"Map table at {zdo.GetPosition():F0}: unsupported data (version {version}, {count} px); left unchanged.");
				_tables[zdo.m_uid] = (zdo.DataRevision, _revision);
				return true;
			}
		}
		bool gained = false;
		bool changed = false;
		for (int i = 0; i < n; i++)
		{
			if (data[Header + i] != 0)
			{
				if (_explored[i] == 0)
				{
					_explored[i] = 1;
					gained = true;
				}
			}
			else if (_explored[i] != 0)
			{
				data[Header + i] = 1;
				changed = true;
			}
		}
		if (gained)
		{
			_revision++;
		}
		if (changed)
		{
			zdo.Set(ZDOVars.s_data, Utils.Compress(data));
			_written++;
		}
		_tables[zdo.m_uid] = (zdo.DataRevision, _revision);
		return true;
	}

	private void Load()
	{
		try
		{
			if (!File.Exists(_saveFile))
			{
				return;
			}
			byte[] data = Utils.Decompress(File.ReadAllBytes(_saveFile));
			if (data.Length != _explored.Length)
			{
				Logger.LogWarning($"{_saveFile} is for a {Mathf.Sqrt(data.Length):0} px map; ignored.");
				return;
			}
			data.CopyTo(_explored, 0);
			Logger.LogInfo($"Loaded {Explored():N0} explored map pixels from {_saveFile}");
		}
		catch (Exception ex)
		{
			Logger.LogError($"Could not load {_saveFile}: {ex.Message}");
		}
	}

	private void Save()
	{
		if (_explored == null || _savedRevision == _revision)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_saveFile));
			string temp = _saveFile + ".tmp";
			File.WriteAllBytes(temp, Utils.Compress(_explored));
			if (File.Exists(_saveFile))
			{
				File.Delete(_saveFile);
			}
			File.Move(temp, _saveFile);
			_savedRevision = _revision;
		}
		catch (Exception ex)
		{
			Logger.LogError($"Could not save {_saveFile}: {ex.Message}");
		}
	}

	private void OnDestroy()
	{
		Save();
	}

	private int Explored()
	{
		int count = 0;
		foreach (byte b in _explored)
		{
			count += b;
		}
		return count;
	}

	private void Run(Terminal.ConsoleEventArgs args)
	{
		Terminal output = args.Context;
		if (ZNet.instance == null || !ZNet.instance.IsServer())
		{
			Print(output, "mapexplore must run on the server: type 'server mapexplore' instead.");
			return;
		}
		if (_explored == null)
		{
			Print(output, "Shared exploration has not started yet (disabled, or the world is still loading).");
			return;
		}
		if (args.Args.Skip(1).Any((string a) => a.Equals("update", StringComparison.OrdinalIgnoreCase)))
		{
			_tableTimer = float.MaxValue;
			Print(output, "Map tables will be updated within a second.");
		}
		Print(output, $"Shared exploration: {Explored() * 100.0 / _explored.Length:0.00}% of the map explored, {_tableCount} map table(s), updated every {_tableInterval.Value:0} s.");
	}

	private static void Print(Terminal output, string text)
	{
		// ServerDevcommands forwards console output to the caller unless it starts with '['.
		(output ?? (Terminal)Console.instance)?.AddString(text);
	}
}
