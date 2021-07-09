/*
 * R e a d m e
 * -----------
 * 
 * In this file you can include any instructions or other comments you want to have injected onto the 
 * top of your final script. You can safely delete this file if you do not want any such comments.
 */

private static string protocoll_version = "v2.3.9";
		public Program() {
	instance = this;
	Runtime.UpdateFrequency = UpdateFrequency.Update10;
	init();
}

public void Save() {
	if (StorageData != null) Storage = StorageData.save();
}

public static Program instance;
private static Random rnd = new Random();
private const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
private const Int32 MAX_STORAGE = 1073741824;

private string[] validServices = new string[] { "CLIENT", "DNS", "STORAGE" };

// Data Storage
private StringStorage BlockStorage;
private StringStorage DataStorage;
private RandomAcessStorage StorageData;

private String[] debugmessages = new string[8];

// Communication
private IMyBroadcastListener broadcastListener;
private IMyUnicastListener unicastListener;

private List<RemoteInstance> knownIDsInRange = new List<RemoteInstance>();

private Dictionary<string, long> dnscache = new Dictionary<string, long>();
private Dictionary<string, List<string>> awaitDNSCMD = new Dictionary<string, List<string>>();
private RemoteInstance dnsServer = null;

// DisplayBlock, DisplaySlot
private List<COMDisplay> ComDisplays = new List<COMDisplay>();
private IMyTextSurface PBSurface;

// Selecions
private int currentSelection = 0;
private int packetcounter = 0;
private int icmppacketcounter = 0;
private String lastPacket = "";
private String commandResult = "";

// Settings
private String ID;
private String BroadcastTag;
private String UnicastTag;
private String ServiceInstance;
private String DisplayName;
private Boolean AutoPing;
private Boolean ShowRawData;
private Boolean PacketLogging;
private Boolean CloneDisplayDataTOPB;
private Boolean DisplayCaching;
private int AccessRadius;
private Boolean m_Silent;
private Boolean Silent {
	get { return m_Silent; }
	set {
		m_Silent = value;
		BlockStorage.Set("03_Value_Silent", value.ToString());
	}
}

// Runtime Untility Variables
private IMyTextSurface PBKeyboard;
private int loadingAnimation = 0;
private int instructions = -1;
private int cycle;
private Boolean errored = false, warning = false;
private StringBuilder packetlogs = new StringBuilder();

public void Main(string argument, UpdateType updateSource) {
	try {
		//if (!updateSource.ToString().StartsWith("Update")) commandResult = updateSource.ToString();
		if (updateSource.ToString().StartsWith("Update")) {
			if (AutoPing && !Silent && cycle % 30 == 0)
				SendPing();
			cycle++;
			RefreshDisplays(); // timing is set in the function
			if (awaitDNSCMD.Count > 0 && !ServiceInstance.Equals("DNS") && cycle % 20 == 0) DnsAwaitSend();
			if (cycle % 60 == 0) RemoteInstance.Check();
		} else if (updateSource.Equals(UpdateType.IGC)) {
			OnReceiveCheck();
		} else if (updateSource.Equals(UpdateType.Terminal) || updateSource.Equals(UpdateType.Trigger) || updateSource.Equals(UpdateType.Script)) {
			if (argument == "init") init();
			else CommandInput(argument, updateSource);
		}
	} catch (Exception ex) {
		BlockStorage.Set("99_LatestExceptionInMain", ex.Message + "\n" + ex.StackTrace);
		errored = true;
	}
}
private void init() {
	// Load Settings
	BlockStorage = new StringStorage((IMyTerminalBlock)Me);
	DataStorage = new StringStorage(Storage);

	loadID(false);
	LoadSettings();

	if (ServiceInstance.Equals("STORAGE")) {
		StorageData = new RandomAcessStorage(Storage);
	}

	if (ServiceInstance.Equals("DNS")) DNSLoad();

	PBSurface = Me.GetSurface(0);
	PBSurface.ContentType = ContentType.TEXT_AND_IMAGE;

	List<IMyTextSurfaceProvider> tmpblocks = new List<IMyTextSurfaceProvider>();
	List<IMyTextSurfaceProvider> blocks = new List<IMyTextSurfaceProvider>();
	GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(tmpblocks);

	// Scan Display Blocks
	foreach (IMyTextSurfaceProvider block in tmpblocks) {
		if (((IMyTerminalBlock)block).CustomName.Contains("[COM]")) {
			new COMDisplay(block);
		}
	}

	// Communication
	broadcastListener = IGC.RegisterBroadcastListener(BroadcastTag);
	broadcastListener.SetMessageCallback(BroadcastTag);
	unicastListener = IGC.UnicastListener;
	unicastListener.SetMessageCallback(UnicastTag);

	PBKeyboard = Me.GetSurface(1);
	PBKeyboard.ContentType = ContentType.TEXT_AND_IMAGE;
	PBKeyboard.FontSize = 4.5f;
	PBKeyboard.Alignment = TextAlignment.CENTER;
}
private void loadID(Boolean reinit) {
	ID = BlockStorage.Get("02_Setting_ID", RandomString(8));
}
private void LoadSettings() {
	BlockStorage.Set("00_INFO1", "99 is used for Errors");
	BlockStorage.Set("00_INFO2", "97 is used for Warnings");
	BlockStorage.Set("00_INFO2", "98 Can be used for Temp Values");
	AccessRadius = Int32.Parse(BlockStorage.Get("02_Setting_AccessRadius", "-1"));
	AutoPing = Boolean.Parse(BlockStorage.Get("02_Setting_AutoPing", "true"));
	ShowRawData = Boolean.Parse(BlockStorage.Get("02_Setting_ShowRawData", "false"));
	Silent = Boolean.Parse(BlockStorage.Get("03_Value_Silent", "false"));
	PacketLogging = Boolean.Parse(BlockStorage.Get("02_Setting_PacketLogging", "false"));
	CloneDisplayDataTOPB = Boolean.Parse(BlockStorage.Get("02_Setting_CloneDisplayDataTOPB", "false"));
	DisplayCaching = Boolean.Parse(BlockStorage.Get("02_Setting_DisplayCaching", "false"));
	BroadcastTag = BlockStorage.Get("02_Setting_Tag-Broadcast", "COM-NET");
	UnicastTag = BlockStorage.Get("02_Setting_Tag-Unicast", "COM-NETU");
	ServiceInstance = BlockStorage.Get("02_Setting_Service", "CLIENT");
	DisplayName = BlockStorage.Get("02_Setting_DisplayName", Me.CubeGrid.CustomName);

	// Check if ServiceName is valid
	Boolean a = false;
	for (int i = 0; i < validServices.Length; i++) if (validServices[i].Equals(ServiceInstance)) a = true;
	if (!a) {
		ServiceInstance = "CLIENT";
		BlockStorage.Set("Service", "CLIENT");
	}
	ClearSettings(BlockStorage, "99");
	ClearSettings(BlockStorage, "98");
	ClearSettings(BlockStorage, "97");
}
private void RefreshDisplays() {
	if (cycle % 5 == 0) {
		StringBuilder message = new StringBuilder();
		StringBuilder displayMessage = new StringBuilder();

		if (ShowRawData) displayMessage.Append($"\nCMD-Result: {commandResult}");
		int i = 0;
		knownIDsInRange.Clear();

		foreach (RemoteInstance instance in RemoteInstance.GetList()) {
			String cache = instance.DisplayCache;
			if (DisplayCaching && cache != null) {
				displayMessage.Append(cache);
				knownIDsInRange.Add(instance);
				continue;
			}
			StringBuilder builder = new StringBuilder();

			TimeSpan span = DateTime.Now.Subtract(new DateTime(instance.LastUpdated));
			long time = (span.Ticks / 10000000);
			double distance = Vector3D.Distance(instance.Position, Me.GetPosition());
			String nameTag = ShowRawData ? instance.DisplayName + " ( " + instance.ID + " )" : instance.DisplayName;

			if ((distance <= AccessRadius || AccessRadius == -1) && time < 30) {
				if (instance.Selected) {
					nameTag = ">>" + nameTag + "<<";
				} else if (i == currentSelection) {
					nameTag = ">" + nameTag + "<";
				}
				knownIDsInRange.Add(instance);
				builder.Append($"\n{nameTag}");
				i++;
				if (!instance.Ready) {
					builder.Append($"\n    Pending Informations " + GetLoadingAnimation(loadingAnimation));
					continue;
				}
				if (ShowRawData) {
					List<String> registeredDNSEntrys = getDNSOfID(instance.ID);
					builder.Append($"\n    Protocoll: {instance.ProtocollVersion} | Service: {instance.Service} | Distance: {Math.Round(distance, 3)}m");
					if (registeredDNSEntrys.Count > 0) {
						builder.Append("Known DNS:");
						foreach (string entry in registeredDNSEntrys) {
							builder.Append($"{entry},");
						}
					}
					List<Transmission> transmissions = Transmission.GetTransmissions(instance.ID);
					if (transmissions.Count > 0) {
						builder.Append("\nTransmissions: ");
						foreach (Transmission transmission in transmissions) {
							builder.Append($"\n    {transmission.transmissionID} -> {transmission.GetProgress()}");
							String output;
							if (transmission.GetReceivedData(out output)) {
								builder.Append($"\n        -> {output}");
							}
						}
					}

					if (instance.LastResult != null && instance.LastResult.Length > 0) builder.Append($"\n    Result: {instance.LastResult}");
				}
			}
			instance.DisplayCache = builder.ToString();
			displayMessage.Append(builder.ToString());
		}

		foreach (COMDisplay display in COMDisplay.Displays) {
			IMyTextSurface surface = display.Surface.GetSurface(display.Slot);
			surface.ContentType = ContentType.TEXT_AND_IMAGE;
			if (display.AutoResize) {
				surface.FontSize = (ShowRawData ? 0.5f : 1.0f);
			}
			surface.WriteText(displayMessage.Length > 0 ? displayMessage.ToString().Substring(1) : "");
			if (errored) {
				surface.FontColor = Color.Red;
			} else if (warning) {
				surface.FontColor = Color.Orange;
			} else {
				surface.FontColor = Color.White;
			}
		}

		int cnt = Runtime.CurrentInstructionCount;
		if (instructions < cnt) instructions = cnt;
		message
			.Append($"\nProtocollVersion >> {protocoll_version}")
			.Append($"\nCur/Max(Peak) InstCnt >> {Runtime.CurrentInstructionCount}/{Runtime.MaxInstructionCount} ({instructions})")
			.Append($"\nKnownIDs(In Range) >> {RemoteInstance.GetList().Count.ToString()} ({knownIDsInRange.Count.ToString()})")
			;
		if (!ServiceInstance.Equals("DNS")) {
			message.Append($"\nDNSServer -> {(dnsServer == null ? "N/A" : dnsServer.ID.ToString())}");
		}

		if (ShowRawData) {
			message
				.Append($"\nAwaitDNS >> {awaitDNSCMD.Count}")
				.Append($"\nDNS Entrys >> {dnscache.Count}")
				//.Append($"\nPackets >> {packetcounter}")
				//.Append($"\nICMP Packets >> {icmppacketcounter}")
				;
			switch (ServiceInstance) {
				case "DNS":
					message.Append($"\nStorage >> {Storage.Length}");
					foreach (KeyValuePair<string, long> dnsentrys in dnscache) {
						message.Append($"\n    {dnsentrys.Key} -> {dnsentrys.Value}");
					}

					break;
				case "STORAGE":
					double percent = Storage.Length / (double)MAX_STORAGE;
					message.Append($"\nStorage ({Math.Round(percent, 10)}%) {Storage.Length} Bytes");
					break;
			}
			message.Append($"\nLastPacket -> {lastPacket}");
			for (int x = 0; x < debugmessages.Length; x++) {
				if (debugmessages[x] == null || debugmessages[x].Length == 0) continue;
				message.Append($"\n    {x} -> {debugmessages[x]}");
			}
		}
		if (CloneDisplayDataTOPB) message.Append($"\nKnownIDs:").Append(displayMessage.ToString());
		PBSurface.WriteText(message.ToString().Substring(1));

		if (PacketLogging) {
			Echo(packetlogs.ToString().Replace(" ", ""));
		} else packetlogs = new StringBuilder();
	}
	if (errored) {
		PBKeyboard.FontColor = Color.Red;
		PBKeyboard.BackgroundColor = Color.Black;
		/*
				if (cycle % 6 > 3) {
					PBKeyboard.FontColor = Color.Red;
					PBKeyboard.BackgroundColor = Color.Black;
				} else {
					PBKeyboard.FontColor = Color.White;
					PBKeyboard.BackgroundColor = Color.Red;
				}
				*/
	} else if (warning) {
		PBKeyboard.FontColor = Color.Orange;
		PBKeyboard.BackgroundColor = Color.Black;
	} else {
		PBKeyboard.FontColor = Color.White;
		PBKeyboard.BackgroundColor = Color.Black;
	}
	PBKeyboard.WriteText(GetLoadingAnimation(loadingAnimation) + "\n[" + ServiceInstance + "]\n" + GetLoadingAnimation(loadingAnimation));
	if (loadingAnimation >= 3) loadingAnimation = 0;
	else loadingAnimation++;
}
private void DNSLoad() {
	foreach (KeyValuePair<string, string> DNSEntry in DataStorage.GetRaw()) {
		dnscache.Add(DNSEntry.Key, long.Parse(DNSEntry.Value));
	}
}
private void CommandInput(string argument, UpdateType updateSource) {
	String[] args = argument.Split(';');
	String header = null;
	if (updateSource.Equals(UpdateType.Script)) {
		commandResult = "Failed No Info";
		StringStorage stringStorage = new StringStorage("");
		stringStorage.Set("sourcepb", args[0]);
		header = stringStorage.ToString();
		String[] tmp = new String[args.Length];
		Array.Copy(args, 0, tmp, 0, args.Length);
		args = new string[tmp.Length - 1];
		Array.Copy(tmp, 1, args, 0, args.Length);
	}

	switch (args[0].ToLower()) {
		case "selup":
			if (currentSelection++ >= knownIDsInRange.Count - 1) currentSelection = 0;
			RefreshDisplays();
			break;

		case "seldown":
			if (currentSelection-- <= 0) currentSelection = knownIDsInRange.Count - 1;
			RefreshDisplays();
			break;

		case "select":
			int c = 0;
			foreach (RemoteInstance instance in knownIDsInRange) {
				if (c++ == currentSelection) {
					instance.Selected = true;

				} else instance.Selected = false;
			}
			RefreshDisplays();
			break;
		case "silent":
			Silent = !Silent;
			if (Silent) SendMessage("rem\n" + ID);
			else SendPing();
			break;
		case "sendpacket":
			if (args.Length > 3) {
				string[] a = new string[args.Length - 3];
				for (int i = 0; i < a.Length; i++) a[i] = args[i + 3];
				commandResult = "sending packet " + a.Length;
				SendPacket(args[1], header, args[2], a);
			} else commandResult = "Incorrect syntax for sendpacket!";
			break;
		case "applyaction": {
				Object TargetID;
				if (args[1].ToLower().Equals("null")) {
					RemoteInstance selected;
					if ((selected = RemoteInstance.GetSelected()) != null) {
						TargetID = selected.ID;
					} else {
						commandResult = "No Instance Selected";
						break;
					}
				} else TargetID = args[1];
				String Mode = args[2].ToLower().Equals("g") ? "group" : "block";
				String Target = args[3];
				String Action = args[4];

				commandResult = "A: " + argument;
				SendPacket(TargetID, header, "applyaction", Mode, Target, Action);
				break;
			}
		case "adddns":
			SendPacket(dnsServer.ID, header, "dns", "addentry", args[1], args[2]);
			break;
		case "data":
			break;
		case "speedtest":
			/*
					Echo(Storage.Length + "");
					char[] data = new char[4];
					data[0] = (char)0xf0;
					data[1] = (char)0x9f;
					data[2] = (char)0x98;
					data[3] = (char)0x80;
					StorageData.write(data, 0);
					Storage = StorageData.save();
					StorageData = new RandomAcessStorage(Storage);
					//DataStorage = new StringStorage(Storage);
					*/
			break;
		case "test":
			//SendPacket(dnsServer.ID, null, "masssend", new string(new char[Int32.Parse(args[1])]));
			BlockStorage.Set("97_debug", GetHex(Storage));
			break;
		case "test2":
			char[] n = new char[256];
			for (int i = 0; i < 256; i++) {
				n[i] = (char)i;
			}
			BlockStorage.Set("98_test", new string(n));
			break;
		case "delstor":
			Storage = "";
			break;
		case "clspklog":
			packetlogs = new StringBuilder();
			break;
		case "load":
			LoadSettings();
			break;
		case "quiterrors":
			ClearSettings(BlockStorage, "99");
			break;
	}
}
private void ClearSettings(StringStorage storage, String prefix) {
	errored = false;
	List<string> toRemove = new List<string>();
	foreach (KeyValuePair<string, string> pair in storage.GetRaw()) {
		int num;
		if (Int32.TryParse(pair.Key.Substring(0, 2), out num)) {
			if (pair.Key.StartsWith(prefix)) toRemove.Add(pair.Key);
		} else {
			toRemove.Add(pair.Key);
		}
	}
	BlockStorage.Remove(toRemove);
}
private void DnsAwaitSend() {
	try {
		List<String> toRemove = new List<String>();
		foreach (String dnsname in awaitDNSCMD.Keys) {
			if (dnscache.ContainsKey(dnsname)) {
				List<String> a;
				awaitDNSCMD.TryGetValue(dnsname, out a);

				foreach (String b in a) {
					String[] c = b.Split('\n')[1].Split(';');
					String[] args = new string[c.Length - 1];
					String cmd = c[0];
					for (int i = 1; i < c.Length; i++) {
						args[i - 1] = c[i];
					}
					SendPacket(dnsname, null, cmd, args);
				}
				toRemove.Add(dnsname);
			}
		}
		foreach (String a in toRemove) {
			awaitDNSCMD.Remove(a);
		}
	} catch (Exception ex) {
		debugmessages[7] = ex.GetType().Name;
	}
}
private void OnReceiveCheck() {
	if (broadcastListener.HasPendingMessage) {
		MyIGCMessage message = broadcastListener.AcceptMessage();
		if (message.Tag == BroadcastTag && message.Data is string) {
			try {
				OnReceiveBroadcast(message);
			} catch (Exception ex) {
				BlockStorage.Set("99_ERR_RECEIVEBROADCAST", ex.Message + " \n " + ex.StackTrace);
				errored = true;
			}
		}
	}

	if (unicastListener.HasPendingMessage) {
		MyIGCMessage message = unicastListener.AcceptMessage();
		if (message.Tag.StartsWith(UnicastTag) && message.Data is string) {
			try {
				OnReceiveUnicast(message);
			} catch (Exception ex) {
				BlockStorage.Set("99_ERR_RECEIVEUNICAST", ex.Message + " \n " + ex.StackTrace);
				errored = true;
			}
		}
	}
}
private void OnReceiveBroadcast(MyIGCMessage message) {
	string[] data = message.Data.ToString().Split('\n');
	switch (data[0].ToLower()) {
		case "pong": {
				string version = data[1];
				if (!data[1].Equals(protocoll_version)) break; // Check the Protocoll Versions

				long remoteID = message.Source;
				string[] coords = data[2].Split('#');

				Vector3D position = new Vector3D(
					double.Parse(coords[0]),
					double.Parse(coords[1]),
					double.Parse(coords[2])
					);

				RemoteInstance instance = RemoteInstance.GetFromID(remoteID);
				Boolean requestData = false;
				if (instance == null) {
					instance = new RemoteInstance();
					instance.ID = remoteID;
					requestData = true;
				}
				instance.ProtocollVersion = version;
				instance.Position = position;
				instance.Update();
				if (requestData)
					SendPacket(message.Source, null, "getinfo", new string[0]);
			}
			icmppacketcounter++;
			return;
		case "rem":
			RemoteInstance.Remove(RemoteInstance.GetFromID(message.Source));
			packetcounter++;
			return;
	}
}
private Boolean OnReceiveUnicast(MyIGCMessage message) {
	//string[] messagedata = message.Data.ToString().Split('\n');
	string[] messagedata = Arguments.extractFirstArgument(message.Data.ToString(), '\n');
	lastPacket = "";

	// Get RemoteInstance from cache
	RemoteInstance remoteInstance = RemoteInstance.GetFromID(message.Source);
	if (remoteInstance == null) {
		lastPacket += "Unregistered RemoteID, : " + message.Source;
		return false;
	}

	// Only Allows current packet version
	if (!remoteInstance.ProtocollVersion.Equals(protocoll_version)) {
		lastPacket += "Packet Version missmatch, sended with: " + remoteInstance.ProtocollVersion + " needed: " + protocoll_version;
		return false;
	}

	// Creates arguments Array
	Arguments arguments = new Arguments(messagedata[1], ';');
	StringStorage HeaderData = new StringStorage(messagedata[0]);

	// Debug messages
	if (PacketLogging) {
		packetlogs
			.Append("IN: H[ ")
			.Append(HeaderData)
			.Append(" ]D[ ")
			.Append(arguments.GetOther())
			.Append(" ]\n")
			.Append("==========\n")
			;
	}

	String result = "Failed NO Info"; // Default Packet Result
	switch (arguments.GetNext()) {
		case "storage":
			if (ServiceInstance.Equals("STORAGE")) {
				switch (arguments.GetNext()) {
					case "set": {
							//DataStorage.Set(arguments.GetNext(), arguments.GetNext());
							string key = arguments.GetNext(), value = arguments.GetNext();

							StorageData.mft.createFile(key, value.Length);
							StorageData.mft.writeFile(key, 0, value.ToCharArray(), 0, value.Length);
							Save();
							result = "Success";
						}
						break;
					case "get": {
							/*
									string output = DataStorage.Get(arguments.GetNext());
									if (output != "N/A") {
										result = null;
										//SendPacket(message.Source, HeaderData.ToString(), "storage", "getresponse", output);
										Transmission.CreateTransmissionTransmit(message.Source, output.ToArray(), HeaderData.ToString());
									} else result = "Storage Key not Exists";
									*/
							string key = arguments.GetNext();
							int size;
							if ((size = StorageData.mft.getFileSize(key)) != -1) {
								char[] data = new char[size];
								StorageData.mft.readFile(key, 0, data, 0, data.Length);
								Transmission.CreateTransmissionTransmit(message.Source, data, HeaderData.ToString());
							} else result = "Storage Key not Exists";
						}
						break;
					case "remove":
						if (DataStorage.Remove(arguments.GetNext())) result = "Success";
						else result = "Storage Key not Exists";
						break;
				}
			} else {
				if (arguments.GetNext().ToLower().Equals("getresponse")) {
					result = null;
					// TODO:
					debugmessages[0] = arguments.GetNext();
				}
			}
			break;
		case "dns":
			if (ServiceInstance.Equals("DNS")) {
				switch (arguments.GetNext()) {
					case "resolve": {
							long dnsid;
							String dnsname = arguments.GetNext();
							if (dnscache.TryGetValue(dnsname, out dnsid)) {
								SendPacket(message.Source, HeaderData.ToString(), "dns", "resolved", dnsname, dnsid.ToString());
								result = "Success";
							} else {
								result = "DNSEntry Not Exists";
							}
							break;
						}
					case "addentry": {
							String dnsname = arguments.GetNext();
							String target = arguments.GetNext();
							if (target.ToLower().Equals("null")) target = message.Source.ToString();
							lastPacket += target;
							if (!dnscache.ContainsKey(dnsname)) {
								dnscache.Add(dnsname, long.Parse(target));
								DataStorage.Set(dnsname, target);
								result = "Success";
							} else
								result = "DNSEntry Already Exists";
							break;
						}
					case "removeentry": {
							String dnsname = arguments.GetNext();
							if (dnscache.ContainsKey(dnsname)) {
								dnscache.Remove(dnsname);
								DataStorage.Remove(dnsname);
								result = "Success";
							} else
								result = "DNSEntry Not Exists";
							break;
						}
				}
			} else {
				result = null;
				switch (arguments.GetNext()) {
					case "resolved":
						dnscache.Add(arguments.GetNext(), long.Parse(arguments.GetNext()));
						DnsAwaitSend();
						break;
					case "update":
						String s = arguments.GetNext();
						String target = arguments.GetNext();
						if (s.Equals("remove") && dnscache.ContainsKey(target)) dnscache.Remove(target);
						break;
				}
			}
			break;
		case "data":
			string cmd = arguments.GetNext();
			long transmissionID = long.Parse(arguments.GetNext());
			result = null;
			if (cmd.ToLower().Equals("init")) {
				Transmission.CreateTransmissionReceive(message.Source, transmissionID, long.Parse(arguments.GetNext()), HeaderData.ToString());
			} else {
				Transmission trans = Transmission.GetTransmissionByID(transmissionID);
				if (trans == null) {
					result = "Transmission not exists";
					break;
				}
				switch (cmd.ToLower()) {
					case "send":
						trans.Receive(arguments.GetNext().ToArray());
						break;
					case "done":
						trans.Done();
						break;
					case "request":
						trans.Transmit();
						break;
				}
			}
			break;
		case "getinfo": {
				result = null;
				String a1 = arguments.GetNext();
				if (!a1.Equals("result")) {
					Vector3D pos = Me.GetPosition();
					SendPacket(message.Source, HeaderData.ToString(), "getinfo"
						, "result"
						, DisplayName
						, Me.CubeGrid.CustomName
						, pos.X + "#" + pos.Y + "#" + pos.Z
						, ServiceInstance
						);
				} else {
					String a2 = arguments.GetNext();
					String a3 = arguments.GetNext();
					String a4 = arguments.GetNext();
					String a5 = arguments.GetNext();
					string[] coords = a4.Split('#');
					Vector3D position = new Vector3D(
						double.Parse(coords[0]),
						double.Parse(coords[1]),
						double.Parse(coords[2])
						);
					remoteInstance.DisplayName = a2;
					remoteInstance.GridName = a3;
					remoteInstance.Position = position;
					remoteInstance.Service = a5;
					remoteInstance.Ready = true;
					remoteInstance.Update();
					if (a5.ToUpper().Equals("DNS")) {
						dnsServer = remoteInstance;
					}
				}
				break;
			}
		case "applyaction": {
				String a1 = arguments.GetNext();
				String a2 = arguments.GetNext();
				String a3 = arguments.GetNext();
				if (a1.Equals("group")) {
					IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName(a2);
					if (group != null) {
						lastPacket += "\nAllowed";
						List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
						group.GetBlocks(blocks);
						foreach (IMyTerminalBlock s in blocks) {
							if (s != null) s.ApplyAction(a3);
						}
						result = "Success";
					} else {
						result = "Failed->GroupNotFound";
						lastPacket += "\nDenied " + a2;
					}
				} else if (a1.Equals("block")) {
					IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(a2);
					if (block != null) {
						lastPacket += "\nAllowed";
						block.ApplyAction(a3);
						result = "Success";
					} else {
						lastPacket += "\nDenied " + a2;
						result = "Failed->BlockNotFound";
					}
				}
				break;
			}
		case "masssend":
			string msg = arguments.GetNext();
			debugmessages[1] = msg.Length + "";
			break;
		case "result": {
				result = null;
				if (!ServiceInstance.Equals("CLIENT")) break;
				remoteInstance.LastResult = messagedata[1];
				// TODO
			}
			break;
	}
	if (result != null) {
		SendPacket(message.Source, HeaderData.ToString(), "result", result);
	}
	return true;
}
private void OnTransmissionReceive(Transmission transmission) {
	StringStorage headerData = new StringStorage(transmission.headerData);
	String target = headerData.Get("sourcepb");
	if (target != "N/A") {
		long targetid = long.Parse(target);
		debugmessages[1] = "Sending to " + target;
	}
	//transmission.Close();
}
private void SendMessage(string message) {
	IGC.SendBroadcastMessage(BroadcastTag, message);
}
private void SendPacket(Object target, String HeaderData, String command, params String[] data) {
	StringBuilder builder = new StringBuilder();
	for (int i = 0; i < data.Length; i++) builder.Append(";").Append(data[i]);
	String message = data.Length > 0 ? builder.ToString().Substring(1) : "";
	if (PacketLogging) {
		packetlogs
			.Append("OUT: C[ ")
			.Append(command)
			.Append(" ]\nM[ ")
			.Append(message)
			.Append(" ]\n")
			.Append("==========\n")
			;
	}
	long TargetID = 0;
	// Check if Target is an valid ID
	if ((target is long) && RemoteInstance.GetFromID((long)target) != null) {
		TargetID = (long)target;
	} else if (target is string) {
		// Check if DNSEntry is in Cache else resolve DNS Entry
		if (dnscache.ContainsKey((string)target)) {
			dnscache.TryGetValue((string)target, out TargetID);
		} else {
			SendPacket(dnsServer.ID, null, "dns", "resolve", (string)target);
			List<String> commands;

			if (!awaitDNSCMD.TryGetValue((string)target, out commands)) {
				commands = new List<String>();
				awaitDNSCMD.Add((string)target, commands);
			}
			commands.Add((HeaderData != null ? HeaderData : "null") + "\n" + command.ToLower() + ";" + message + "");
			commandResult = "Send DNS Request for -> " + target;
		}
	} else {
		debugmessages[7] = "Successfully Failed!";
		errored = true;
		BlockStorage.Set("99_ERR_SENDPACKET", $"Invalid TargetID Type or failed to check RemoteInstance Storage");
		return;
	}
	IGC.SendUnicastMessage(TargetID, UnicastTag, (HeaderData != null ? HeaderData : "null") + "\n" + command.ToLower() + ";" + message + "");
}
private void SendPing() {
	Vector3D pos = Me.GetPosition();
	SendMessage(
				"pong\n"
				+ protocoll_version + "\n"
				+ pos.X + "#" + pos.Y + "#" + pos.Z + "\n"
				);
}
private static string GetHex(string source) {
	char[] a = source.ToCharArray();
	char[] b = new char[a.Length * 2];
	for (int i = 0; i < a.Length; i ++) {
		b[i * 2]	 = HalfHex(a[i], true);
		b[i * 2 + 1] = HalfHex(a[i], false);
	}

	return new String(b);
}
private static char HalfHex(int c, Boolean a) {
	if (a) c >>= 4;
	return "0123456789ABCDEF"[c&15];
}
private static string RandomString(int length) {
	return new string(Enumerable.Repeat(chars, length)
	  .Select(s => s[rnd.Next(s.Length)]).ToArray());
}
public String GetLoadingAnimation(int i) {
	switch (i) {
		case 0: return "ooo";
		case 1: return "Ooo";
		case 2: return "oOo";
		case 3: return "ooO";
	}
	return "";
}
private List<String> getDNSOfID(long ID) {
	List<String> DNSEntryOfID = new List<string>();
	foreach (KeyValuePair<string, long> dnsentrys in dnscache) {
		if (dnsentrys.Value.Equals(ID)) DNSEntryOfID.Add(dnsentrys.Key);
	}
	return DNSEntryOfID;
}
public class RemoteInstance {
	private static List<RemoteInstance> instances = new List<RemoteInstance>();
	private static RemoteInstance selected;

	public RemoteInstance() {
		instances.Add(this);
	}
	private long _lastUpdated;
	public long LastUpdated {
		get { return _lastUpdated; }
		set { _lastUpdated = value; }
	}

	private long _ID = -1;
	public long ID {
		get { return _ID; }
		set { _ID = value; }
	}

	private string _DisplayName;
	public string DisplayName {
		get { return _DisplayName; }
		set { _DisplayName = value; }
	}

	private string _GridName;
	public string GridName {
		get { return _GridName; }
		set { _GridName = value; }
	}

	private string _ProtocollVersion;
	public string ProtocollVersion {
		get { return _ProtocollVersion; }
		set { _ProtocollVersion = value; }
	}

	private string _Service;
	public string Service {
		get { return _Service; }
		set { _Service = value; }
	}

	private Vector3D _Pos;
	public Vector3D Position {
		get { return _Pos; }
		set { _Pos = value; }
	}

	private Boolean _Selected;
	public Boolean Selected {
		get { return _Selected; }
		set {
			_Selected = value;
			if (value) selected = this;
		}
	}

	private Boolean _Ready = false;
	public Boolean Ready {
		get { return _Ready; }
		set { _Ready = value; }
	}

	private string _LastResult;
	public string LastResult {
		get { return _LastResult; }
		set { _LastResult = value; }
	}

	private String _DisplayCache;
	public String DisplayCache {
		get { return _DisplayCache; }
		set { _DisplayCache = value; }
	}
	public List<RemoteInstance> GetKnownIDs() { return instances; }

	public void Update() {
		LastUpdated = DateTime.Now.Ticks;
		DisplayCache = null;
	}

	public static Boolean Remove(RemoteInstance instance) {
		if (instances.Contains(instance)) {
			instances.Remove(instance);
		}
		return false;
	}

	public static RemoteInstance GetFromID(long id) {
		foreach (RemoteInstance instance in instances) {
			if (instance.ID == id) return instance;
		}
		return null;
	}

	public static List<RemoteInstance> GetList() {
		return instances;
	}

	public static RemoteInstance GetSelected() {
		return selected;
	}

	public static void Check() {
		foreach (RemoteInstance remoteInstance in instances.ToList()) {
			TimeSpan span = DateTime.Now.Subtract(new DateTime(remoteInstance.LastUpdated));
			long time = (span.Ticks / 10000000);
			if (time > 60) Remove(remoteInstance);
			if (!remoteInstance.Ready) instance.SendPacket(remoteInstance.ID, null, "getinfo", new string[0]);
		}
	}

}
public class StringStorage {

	private Dictionary<string, string> storage = new Dictionary<string, string>();
	private IMyTerminalBlock block;
	private Boolean autoSave = true;
	public StringStorage(String s) {
		Load(s);
	}

	public StringStorage(IMyTerminalBlock block) {
		this.block = block;
		Load(block.CustomData);
	}

	public void DisableSave() {
		autoSave = false;
	}

	public void Load(String s) {
		if (s == null) return;
		String[] a = s.Split('\n');
		for (int i = 0; i < a.Length; i++) {
			String[] b = a[i].Split('=');
			if (!storage.ContainsKey(b[0]) && b.Length == 2)
				storage.Add(b[0], b[1]);
		}
	}

	private string GetString() {
		StringBuilder builder = new StringBuilder();
		foreach (KeyValuePair<string, string> data in storage.OrderBy(key => key.Key)) {
			builder
				.Append("\n")
				.Append(data.Key)
				.Append("=")
				.Append(data.Value);
		}
		String s = builder.ToString().Length > 0 ? builder.ToString().Substring(1) : builder.ToString();
		return s;
	}

	public void Save() {
		if (autoSave) {
			if (block == null) {
				instance.Storage = GetString();
			} else block.CustomData = GetString();
		}
	}

	public String Get(String key) {
		String s;
		if (!storage.TryGetValue(key, out s)) s = "N/A";
		return s;
	}

	public String Get(String key, String defaultvalue) {
		String s;
		if (storage.ContainsKey(key)) {
			storage.TryGetValue(key, out s);
		} else {
			s = defaultvalue;
			storage.Add(key, s);
		}
		Save();
		return s;
	}

	public Dictionary<string, string> GetRaw() {
		return storage;
	}

	public void Set(String key, String value) {
		Remove(key);
		storage.Add(key, value);
		Save();
	}

	public Boolean Remove(String key) {
		if (storage.ContainsKey(key)) {
			storage.Remove(key);
			Save();
			return true;
		}
		return false;
	}

	public void Remove(List<string> keys) {
		foreach (string a in keys) {
			if (storage.ContainsKey(a)) {
				storage.Remove(a);
			}
		}
		Save();
	}

	public override string ToString() {
		return GetString();
	}

}
public class COMDisplay {

	private static List<COMDisplay> _Displays = new List<COMDisplay>();
	public COMDisplay(IMyTextSurfaceProvider m_surface) {
		Surface = m_surface;
		CustomData = new StringStorage((IMyTerminalBlock)m_surface);
		if (CustomData.Get("01_Setting_BoundingID", instance.ID).Equals(instance.ID)) {
			Slot = Int32.Parse(CustomData.Get("01_Setting_DisplaySlot", "0"));
			AutoResize = Boolean.Parse(CustomData.Get("01_Setting_AutoResize", "true"));
			_Displays.Add(this);
		}
	}

	private IMyTextSurfaceProvider _Surface;
	public IMyTextSurfaceProvider Surface {
		get { return _Surface; }
		private set { _Surface = value; }
	}
	private Int32 _Slot;
	public Int32 Slot {
		get { return _Slot; }
		private set { _Slot = value; }
	}

	private Boolean _AutoResize;
	public Boolean AutoResize {
		get { return _AutoResize; }
		private set { _AutoResize = value; }
	}

	private StringStorage _CustomData;
	public StringStorage CustomData {
		get { return _CustomData; }
		private set { _CustomData = value; }
	}

	public static List<COMDisplay> Displays {
		get { return _Displays; }
	}


}
public class RandomAcessStorage {
	public RandomAcessStorage_MFT mft;
	char[] database;
	public RandomAcessStorage(String savedState) {
		if (savedState == null) savedState = "";
		database = savedState.ToCharArray();
		mft = new RandomAcessStorage_MFT(this);
	}

	public String save() {
		return new String(database);
	}

	public void write(String w, int offset) {
		w.CopyTo(0, database, offset, w.Length);
	}
	public void write(char[] w, int memoffset) {
		Array.Copy(w, 0, database, memoffset, w.Length);
	}
	public void write(char[] arr, int offset, int len, int memoffset) {
		Array.Copy(arr, 0, database, memoffset, len);
	}

	public void read(char[] arr, int memOffset) {
		read(arr, 0, arr.Length, memOffset);
	}

	public void read(char[] arr, int offset, int len, int memOffset) {
		if (memOffset + len > database.Length) len = database.Length - memOffset;
		Array.Copy(database, memOffset, arr, offset, len);
	}

	public int getDBsize() {
		return database.Length;
	}

	public void resizeDB(int newLen) {
		instance.Echo("resizeDB: " + database.Length + " -> " + newLen);
		char[] oldDB = database;
		database = new char[newLen];
		if (oldDB.Length > 0) {
			if (newLen > oldDB.Length) {
				write(oldDB, 0);
			} else {
				write(oldDB, 0, newLen, 0);
			}
		}
	}

	public int readInt(int offset) {
		char[] raw = new char[4];
		read(raw, offset);
		return System.BitConverter.ToInt32(new byte[] { (byte)raw[0], (byte)raw[1], (byte)raw[2], (byte)raw[3] }, 0);
	}

	public void writeInt(int offset, int i) {
		write(new char[]{
	(char)((i) & 0xFF),
	(char)((i >> 8) & 0xFF),
	(char)((i >> 16) & 0xFF),
	(char)((i >> 24) & 0xFF)
}, offset);
	}
	public int read(int offset) {
		if (offset > database.Length) return 0;
		return database[offset];
	}

	public void write(int offset, int val) {
		if (offset > database.Length) return;
		database[offset] = (char)val;
	}


}
public class RandomAcessStorage_MFT {
	RandomAcessStorage disk;
	int location;
	Dictionary<String, FileLocation> fileMap = new Dictionary<String, FileLocation>();

	public RandomAcessStorage_MFT(RandomAcessStorage disk) {
		this.disk = disk;
		location = disk.readInt(0);
		if (location <= 0) {
			location = 4;
			disk.resizeDB(8);
			fileMap.Add("$MFT", new FileLocation(location, 4));
		} else {
			readMFT();
		}
	}

	public int createFile(String name, int size) {
		if (name.Length > 255) return 1;
		if (size < 0) return 2;
		FileLocation fl = new FileLocation(findNextFileLocation(size), size);
		deleteFile(name);
		fileMap.Add(name, fl);
		writeMFT();
		int requiredLen = getLastFilePos();
		instance.Echo("createFile: " + requiredLen + " " + disk.getDBsize());
		if (requiredLen == fl.position) requiredLen += fl.length;
		if (requiredLen > disk.getDBsize()) {
			disk.resizeDB(requiredLen);
		}
		return -1;
	}

	public void deleteFile(String name) {
		FileLocation fl = null;
		if (fileMap.TryGetValue(name, out fl)) {
			fileMap.Remove(name);
		}
		writeMFT();
		int requiredLen = getLastFilePos();
		if (disk.getDBsize() > requiredLen) {
			disk.resizeDB(requiredLen);
		}
	}

	public void readFile(String name, int fileOffset, char[] data, int dataOffset, int dataLen) {
		FileLocation fl = null;
		if (fileMap.TryGetValue(name, out fl)) {
			disk.read(data, dataOffset, dataLen, fl.position + fileOffset);
		}
	}

	public void writeFile(String name, int fileOffset, char[] data, int dataOffset, int dataLen) {
		FileLocation fl = null;
		if (fileMap.TryGetValue(name, out fl)) {
			if (fileOffset < 0) {
				return;
			}
			if (fileOffset + dataLen > fl.length) {
				dataLen = (fileOffset + dataLen) - fl.length;
				if (dataLen <= 0) return;
			}
			disk.write(data, dataOffset, dataLen, fl.position + fileOffset);
		}
	}

	public List<String> listFiles() {
		List<String> a = new List<String>();
		foreach (String name in fileMap.Keys) a.Add(name);
		return a;
	}

	public int getFileSize(String name) {
		FileLocation fl = null;
		if (fileMap.TryGetValue(name, out fl)) {
			return fl.length;
		}
		return -1;
	}

	public void renameFile(String oldName, String newName) {
		FileLocation fl = null;
		if (fileMap.TryGetValue(newName, out fl)) {
			fileMap.Remove(newName);
		}
		fl = null;
		if (fileMap.TryGetValue(oldName, out fl)) {
			fileMap.Remove(oldName);
			fileMap.Add(newName, fl);
		}
		writeMFT();
	}

	private void readMFT() {
		int count = disk.readInt(location);
		int readPos = location + 4;
		for (int i = 0; i < count; i++) {
			FileLocation fl = new FileLocation(disk, readPos);
			char[] a = new char[disk.read(readPos + 8)];
			disk.read(a, readPos + 9);
			readPos += a.Length + 9;
			fileMap.Add(new String(a), fl);
		}
		fileMap.Add("$MFT", new FileLocation(location, readPos));
	}

	private void writeMFT() {
		int len = 4;
		foreach (String name in fileMap.Keys) {
			if (!name.Equals("$MFT")) len += fileMap[name].calcEntrySize(name);
		}
		int pos = location = findNextFileLocation(len);
		disk.writeInt(0, pos);
		fileMap["$MFT"] = new FileLocation(pos, len);
		int requiredLen = getLastFilePos();
		if (requiredLen == pos) requiredLen += len;
		instance.Echo("writeMFT: " + requiredLen + " " + disk.getDBsize());
		if (requiredLen > disk.getDBsize()) {
			disk.resizeDB(requiredLen);
		}
		instance.Echo("pos=" + pos);
		disk.writeInt(pos, fileMap.Count() - 1);
		pos += 4;
		foreach (String name in fileMap.Keys) {
			if (name.Equals("$MFT")) continue;
			FileLocation fl = fileMap[name];
			disk.writeInt(pos, fl.position);
			disk.writeInt(pos + 4, fl.length);
			char[] a = name.ToCharArray();
			disk.write(pos + 8, a.Length);
			pos += 9;
			disk.write(name, pos);
			pos += a.Length;
		}
	}

	private int getLastFilePos() {
		int pos = 4;
		FileLocation fl;
		while ((fl = getNextFileByOffset(pos)) != null) {
			int a = fl.position + fl.length;
			if (a > pos) pos = a;
		}
		return pos;
	}

	private int findNextFileLocation(int declaredSize) {
		int pos = 4;
		FileLocation fl;
		while ((fl = getNextFileByOffset(pos)) != null) {
			if (fl.position - pos >= declaredSize && fl.length > 0) return pos;
			pos = fl.position + fl.length;
		}
		return pos;
	}

	private FileLocation getNextFileByOffset(int pos) {
		int best = int.MaxValue;
		FileLocation f = null;
		foreach (FileLocation fl in fileMap.Values) {
			int a = fl.length;
			if (a < best && a >= pos) {
				best = a;
				f = fl;
			}
		}
		return f;
	}

	public class FileLocation {
		public int position, length;
		public FileLocation(RandomAcessStorage disk, int memoryOffset) {
			position = disk.readInt(memoryOffset);
			length = disk.readInt(memoryOffset + 4);
		}

		public FileLocation(int position, int length) {
			this.position = position;
			this.length = length;
		}

		public int calcEntrySize(String myName) {
			return myName.Length + 9;
		}
	}
}
public class Transmission {
	private static Dictionary<Transmission, long> runningTransmissions = new Dictionary<Transmission, long>();
	private static Random rand = new Random((int)DateTime.Now.Ticks);

	private const Int32 PACKETSIZE = 200;

	public long targetID;
	public long transmissionID;
	private char[] dataSet;
	private long dataPointer = 0;
	private long lastAccess = DateTime.Now.Ticks;
	public string headerData = "";

	public Boolean receiving = false;
	public Boolean done = false;

	private Transmission(long transmissionID) {
		this.transmissionID = transmissionID;
	}

	public void Transmit() {
		if (receiving) return;
		long bytesToSend = (dataSet.Length - dataPointer < PACKETSIZE ? dataSet.Length - dataPointer : PACKETSIZE);
		char[] tosend = new char[bytesToSend];
		Array.Copy(dataSet, dataPointer, tosend, 0, bytesToSend);
		dataPointer += bytesToSend;
		instance.SendPacket(targetID, headerData, "data", "send", transmissionID.ToString(), new String(tosend));
		if (dataPointer >= dataSet.Length) {
			instance.SendPacket(targetID, headerData, "data", "done", transmissionID.ToString());
			Close();
		}
		lastAccess = DateTime.Now.Ticks;
	}

	public void Receive(char[] chars) {
		if (!receiving) return;
		if (chars.Length + dataPointer > dataSet.Length) {
			instance.BlockStorage.Set("98_DataReceive", $"Failed to Receive DataSet, received {chars.Length} chars, expected {(dataSet.Length - dataPointer)} chars");
			instance.warning = true;
			return;
		}
		Array.Copy(chars, 0, dataSet, dataPointer, chars.Length);
		dataPointer += chars.Length;
		if (dataPointer >= dataSet.Length) {
			instance.SendPacket(targetID, headerData, "data", "done", transmissionID.ToString());
		} else {
			instance.SendPacket(targetID, headerData, "data", "request", transmissionID.ToString());
		}
		lastAccess = DateTime.Now.Ticks;
	}

	public void Close() {
		runningTransmissions.Remove(this);
	}
	public void Done() {
		if (dataSet.Length == dataPointer) {
			done = true;
			instance.OnTransmissionReceive(this);
		} else {
			instance.BlockStorage.Set("98_DataReceive", $"Failed to Complete Transmission, sender called 'Done()' but only {dataPointer}/{dataSet.Length} chars received!");
			instance.warning = true;
		}
	}

	public Boolean GetReceivedData(out string data) {
		if (receiving && dataSet.Length == dataPointer) {
			data = new string(dataSet);
			return true;
		}
		data = null;
		return false;
	}

	public double GetProgress() {
		return Math.Round((100d / (double)dataSet.Length) * (double)dataPointer, 3);
	}

	public static long CreateTransmissionTransmit(long targetID, char[] dataSet, string headerData) {
		long id = RandomLong();
		Transmission transmission = new Transmission(id);
		transmission.targetID = targetID;
		transmission.dataSet = dataSet;
		transmission.headerData = headerData;
		runningTransmissions.Add(transmission, targetID);
		instance.SendPacket(targetID, headerData, "data", "init", id.ToString(), dataSet.Length.ToString());
		return id;
	}

	public static Transmission CreateTransmissionReceive(long sourceID, long transmissionID, long totalBytes, string headerData) {
		Transmission transmission = new Transmission(transmissionID);
		transmission.targetID = sourceID;
		transmission.dataSet = new char[totalBytes];
		transmission.headerData = headerData;
		transmission.receiving = true;
		runningTransmissions.Add(transmission, sourceID);
		instance.SendPacket(sourceID, headerData, "data", "request", transmissionID.ToString());
		return transmission;
	}

	public static void Check() {
		String a = "";
		long timeout = (DateTime.Now.Ticks / 10000000) + 30;
		foreach (KeyValuePair<Transmission, long> transmission in runningTransmissions) {
			if (timeout < transmission.Key.lastAccess / 10000000) {
				a += transmission.Key.transmissionID + " -- ";
			}
		}
		if (a.Length > 0) instance.debugmessages[7] = a;
	}


	public static Dictionary<Transmission, long> GetTransmissions() {
		return runningTransmissions;
	}

	public static List<Transmission> GetTransmissions(long targetID) {
		List<Transmission> trans = new List<Transmission>();
		foreach (KeyValuePair<Transmission, long> transmissions in runningTransmissions) {
			if (transmissions.Value.Equals(targetID)) trans.Add(transmissions.Key);
		}
		return trans;
	}

	public static Transmission GetTransmissionByID(long transmissionID) {
		foreach (KeyValuePair<Transmission, long> transmissions in runningTransmissions) {
			if (transmissions.Key.transmissionID.Equals(transmissionID)) return transmissions.Key;
		}
		return null;
	}

	private static long RandomLong() {
		return ((rand.Next(Int32.MinValue, Int32.MaxValue) & 0xFFFFFFFF) << 32) | (rand.Next(Int32.MinValue, Int32.MaxValue) & 0xFFFFFFFF);
	}
}
public class Arguments {
	private String arg;
	private char split;

	public Arguments(String s, char split) {
		arg = s;
		this.split = split;
	}

	public String GetNext() {
		String[] a = extractFirstArgument(arg, split);
		arg = a[1];
		return a[0];
	}

	public Boolean HasNext() {
		return arg == null;
	}

	public String GetOther() {
		return arg;
	}

	public static String[] extractFirstArgument(String source, char filter) {
		if (source == null) return new String[] { source, null };
		int p = source.IndexOf(filter);
		if (p == -1) return new String[] { source, null };
		return new String[] { source.Substring(0, p), source.Substring(p + 1) };
	}
}