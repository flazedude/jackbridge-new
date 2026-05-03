import Foundation
import NetworkExtension
import SystemExtensions
import Combine

class JackBridgeViewModel: NSObject, ObservableObject {
    enum ProxyEngine: String {
        case external
        case builtIn
    }

    @Published var connections: [ConnectionLog] = []
    @Published var activityLogs: [ActivityLog] = []
    @Published var isProxyActive = false
    @Published var isTrafficLoggingEnabled = true
    @Published var proxyEngine: ProxyEngine = .external
    @Published var builtInProxyConfig = BuiltInProxyConfig()
    
    var tunnelSession: NETunnelProviderSession?
    private var logTimer: Timer?
    private(set) var proxyConfig: ProxyConfig?
    private var mihomoProcess: Process?
    
    private let maxLogEntries = 1000
    // trim to 80% when limit hit to avoid trimming on each entry
    private let trimToEntries = 800
    private let logPollingInterval = 1.0
    private let extensionIdentifier = "com.interceptsuite.JackBridge.extension"
    // reuse formatter
    // saves memory about 2% and speed up the ui
    private let timestampFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss"
        return f
    }()
    // removed uuid and use int - memory usage and speed improved due to size
    private var connectionIdCounter: Int = 0
    private var activityIdCounter: Int = 0
    
    struct ProxyConfig {
        let type: String
        let host: String
        let port: Int
        let username: String?
        let password: String?
    }

    struct BuiltInProxyConfig {
        var corePath: String = "core/mihomo"
        var subscriptionUrl: String = ""
        var localYamlPath: String = ""
        var activeProfilePath: String = "profiles/mihomo.yaml"
        var mixedPort: Int = 7892
        var controllerPort: Int = 9090
        var controllerSecret: String = ""
        var autoUpdateSubscription: Bool = false
    }
    
    struct ConnectionLog: Identifiable {
        let id: Int
        let timestamp: String
        let connectionProtocol: String
        let process: String
        let destination: String
        let port: String
        let proxy: String
    }
    
    struct ActivityLog: Identifiable {
        let id: Int
        let timestamp: String
        let level: String
        let message: String
    }
    
    override init() {
        super.init()
        loadTrafficLoggingSetting()
        loadProxyEngineConfig()
        loadProxyConfig()
        installAndStartProxy()
    }
    
    private func loadTrafficLoggingSetting() {
        isTrafficLoggingEnabled = UserDefaults.standard.object(forKey: "trafficLoggingEnabled") as? Bool ?? true
    }
    
    func toggleTrafficLogging() {
        isTrafficLoggingEnabled.toggle()
        UserDefaults.standard.set(isTrafficLoggingEnabled, forKey: "trafficLoggingEnabled")
        sendTrafficLoggingToExtension(isTrafficLoggingEnabled)
        
        if isTrafficLoggingEnabled {
            startLogPollingTimer()
        } else {
            logTimer?.invalidate()
            logTimer = nil
        }
    }
    
    private func sendTrafficLoggingToExtension(_ enabled: Bool) {
        guard let session = tunnelSession else { return }
        
        let message: [String: Any] = [
            "action": "setTrafficLogging",
            "enabled": enabled
        ]
        
        guard let data = try? JSONSerialization.data(withJSONObject: message) else { return }
        
        try? session.sendProviderMessage(data) { _ in }
    }
    
    private func loadProxyConfig() {
        if let type = UserDefaults.standard.string(forKey: "proxyType"),
           let host = UserDefaults.standard.string(forKey: "proxyHost"),
           let port = UserDefaults.standard.object(forKey: "proxyPort") as? Int {
            let username = UserDefaults.standard.string(forKey: "proxyUsername")
            let password = UserDefaults.standard.string(forKey: "proxyPassword")
            
            proxyConfig = ProxyConfig(
                type: type,
                host: host,
                port: port,
                username: username,
                password: password
            )
        }
    }

    private func loadProxyEngineConfig() {
        if let storedEngine = UserDefaults.standard.string(forKey: "proxyEngine"),
           let engine = ProxyEngine(rawValue: storedEngine) {
            proxyEngine = engine
        }

        var config = BuiltInProxyConfig()
        config.corePath = UserDefaults.standard.string(forKey: "builtInCorePath") ?? config.corePath
        config.subscriptionUrl = UserDefaults.standard.string(forKey: "builtInSubscriptionUrl") ?? ""
        config.localYamlPath = UserDefaults.standard.string(forKey: "builtInLocalYamlPath") ?? ""
        config.activeProfilePath = UserDefaults.standard.string(forKey: "builtInActiveProfilePath") ?? config.activeProfilePath

        if UserDefaults.standard.object(forKey: "builtInMixedPort") != nil {
            config.mixedPort = UserDefaults.standard.integer(forKey: "builtInMixedPort")
        }

        if UserDefaults.standard.object(forKey: "builtInControllerPort") != nil {
            config.controllerPort = UserDefaults.standard.integer(forKey: "builtInControllerPort")
        }

        config.controllerSecret = UserDefaults.standard.string(forKey: "builtInControllerSecret") ?? ""
        config.autoUpdateSubscription = UserDefaults.standard.object(forKey: "builtInAutoUpdateSubscription") as? Bool ?? false
        builtInProxyConfig = config
    }

    private func saveProxyEngineConfig() {
        UserDefaults.standard.set(proxyEngine.rawValue, forKey: "proxyEngine")
        UserDefaults.standard.set(builtInProxyConfig.corePath, forKey: "builtInCorePath")
        UserDefaults.standard.set(builtInProxyConfig.subscriptionUrl, forKey: "builtInSubscriptionUrl")
        UserDefaults.standard.set(builtInProxyConfig.localYamlPath, forKey: "builtInLocalYamlPath")
        UserDefaults.standard.set(builtInProxyConfig.activeProfilePath, forKey: "builtInActiveProfilePath")
        UserDefaults.standard.set(builtInProxyConfig.mixedPort, forKey: "builtInMixedPort")
        UserDefaults.standard.set(builtInProxyConfig.controllerPort, forKey: "builtInControllerPort")
        UserDefaults.standard.set(builtInProxyConfig.controllerSecret, forKey: "builtInControllerSecret")
        UserDefaults.standard.set(builtInProxyConfig.autoUpdateSubscription, forKey: "builtInAutoUpdateSubscription")
    }
    
    private func saveProxyConfig(_ config: ProxyConfig) {
        UserDefaults.standard.set(config.type, forKey: "proxyType")
        UserDefaults.standard.set(config.host, forKey: "proxyHost")
        UserDefaults.standard.set(config.port, forKey: "proxyPort")
        
        if let username = config.username {
            UserDefaults.standard.set(username, forKey: "proxyUsername")
        } else {
            UserDefaults.standard.removeObject(forKey: "proxyUsername")
        }
        
        if let password = config.password {
            UserDefaults.standard.set(password, forKey: "proxyPassword")
        } else {
            UserDefaults.standard.removeObject(forKey: "proxyPassword")
        }
    }
    
    private func installAndStartProxy() {
        // Stop any existing tunnel first so macOS replaces the running extension
        // binary with the newly installed one instead of reusing the old cached process.
        NETransparentProxyManager.loadAllFromPreferences { [weak self] managers, error in
            guard let self = self else { return }
            
            if let existing = managers?.first,
               let session = existing.connection as? NETunnelProviderSession,
               session.status != .disconnected && session.status != .invalid {
                session.stopTunnel()
                // Brief pause to let the old extension fully terminate
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.8) {
                    self.submitExtensionActivationRequest()
                }
            } else {
                self.submitExtensionActivationRequest()
            }
        }
    }
    
    private func submitExtensionActivationRequest() {
        let request = OSSystemExtensionRequest.activationRequest(
            forExtensionWithIdentifier: extensionIdentifier,
            queue: .main
        )
        request.delegate = self
        OSSystemExtensionManager.shared.submitRequest(request)
    }
    
    func startProxy() {
        NETransparentProxyManager.loadAllFromPreferences { [weak self] managers, error in
            guard let self = self else { return }
            
            if let error = error {
                self.addLog("ERROR", "Failed to load managers: \(error.localizedDescription)")
                return
            }
            
            let manager = managers?.first ?? NETransparentProxyManager()
            manager.localizedDescription = "JackBridge Transparent Proxy"
            manager.isEnabled = true
            
            let providerProtocol = NETunnelProviderProtocol()
            providerProtocol.providerBundleIdentifier = self.extensionIdentifier
            providerProtocol.serverAddress = "JackBridge"
            manager.protocolConfiguration = providerProtocol
            
            manager.saveToPreferences { saveError in
                if let saveError = saveError {
                    self.addLog("ERROR", "Failed to save preferences: \(saveError.localizedDescription)")
                    return
                }
                
                self.addLog("INFO", "Configuration saved")
                self.reloadAndStartTunnel(manager: manager)
            }
        }
    }
    
    private func reloadAndStartTunnel(manager: NETransparentProxyManager) {
        manager.loadFromPreferences { [weak self] loadError in
            guard let self = self else { return }
            
            if let loadError = loadError {
                self.addLog("ERROR", "Failed to reload preferences: \(loadError.localizedDescription)")
                return
            }
            
            do {
                try (manager.connection as? NETunnelProviderSession)?.startTunnel()
                
                DispatchQueue.main.async {
                    self.isProxyActive = true
                    self.addLog("INFO", "Proxy tunnel started")
                }
                
                if let session = manager.connection as? NETunnelProviderSession {
                    // Wait a moment for tunnel to be ready, then configure
                    DispatchQueue.global().asyncAfter(deadline: .now() + 1.5) {
                        self.setupLogPolling(session: session)
                        
                        if self.proxyEngine == .builtIn {
                            self.startBuiltInProxy { success in
                                guard success else { return }
                                let config = ProxyConfig(
                                    type: "socks5",
                                    host: "127.0.0.1",
                                    port: self.builtInProxyConfig.mixedPort,
                                    username: nil,
                                    password: nil
                                )
                                self.sendProxyConfigToExtension(config, session: session)
                            }
                        } else if let config = self.proxyConfig {
                            self.sendProxyConfigToExtension(config, session: session)
                        }
                        
                        RuleManager.loadRulesFromUserDefaults(session: session) { success, count in
                            if success && count > 0 {
                                self.addLog("INFO", "Loaded \(count) rule(s) from local storage")
                            }
                        }
                    }
                }
            } catch {
                self.addLog("ERROR", "Failed to start tunnel: \(error.localizedDescription)")
            }
        }
    }
    
    func stopProxy() {
        guard let session = tunnelSession else {
            isProxyActive = false
            logTimer?.invalidate()
            logTimer = nil
            return
        }
        
        // Clear all data from extension memory before stopping
        clearExtensionMemory(session: session) { [weak self] in
            guard let self = self else { return }
            
            NETransparentProxyManager.loadAllFromPreferences { managers, error in
                if let manager = managers?.first {
                    (manager.connection as? NETunnelProviderSession)?.stopTunnel()
                    self.isProxyActive = false
                    self.logTimer?.invalidate()
                    self.logTimer = nil
                    self.tunnelSession = nil
                    self.stopBuiltInProxy()
                    self.addLog("INFO", "Proxy stopped and extension memory cleared")
                }
            }
        }
    }
    
    private func clearExtensionMemory(session: NETunnelProviderSession, completion: @escaping () -> Void) {
        // clear rules auto fix the #51 - and proxy rules become inactive after It closes
        RuleManager.clearRules(session: session) { success, message in
            //clear proxy config as well keep meory usage low for extesion 
            let clearConfigMessage: [String: Any] = [
                "action": "clearConfig"
            ]
            
            guard let data = try? JSONSerialization.data(withJSONObject: clearConfigMessage) else {
                completion()
                return
            }
            
            try? session.sendProviderMessage(data) { _ in
                completion()
            }
        }
    }
    
    private func setupLogPolling(session: NETunnelProviderSession) {
        tunnelSession = session
        
        sendTrafficLoggingToExtension(isTrafficLoggingEnabled)
        
        if isTrafficLoggingEnabled {
            startLogPollingTimer()
        }
    }
    
    private func startLogPollingTimer() {
        DispatchQueue.main.async { [weak self] in
            guard let self = self else { return }
            self.logTimer?.invalidate()
            self.logTimer = Timer.scheduledTimer(
                withTimeInterval: self.logPollingInterval,
                repeats: true
            ) { [weak self] _ in
                self?.pollLogs()
            }
        }
    }
    
    private func pollLogs() {
        guard let session = tunnelSession else { return }
        
        let message = ["action": "getLogs"]
        guard let data = try? JSONSerialization.data(withJSONObject: message) else { return }
        
        try? session.sendProviderMessage(data) { [weak self] response in
            guard let self = self,
                  let responseData = response else {
                return
            }
            
            if let logs = try? JSONSerialization.jsonObject(with: responseData) as? [[String: String]] {
                DispatchQueue.main.async {
                    for log in logs {
                        if log["type"] == "connection" {
                            self.handleConnectionLog(log)
                        } else {
                            self.handleActivityLog(log)
                        }
                    }
                }
            }
        }
    }
    
    private func handleConnectionLog(_ log: [String: String]) {
        guard isTrafficLoggingEnabled else { return }
        
        guard let proto = log["protocol"],
              let process = log["process"],
              let dest = log["destination"],
              let port = log["port"],
              let proxy = log["proxy"] else {
            return
        }
        
        connectionIdCounter &+= 1
        let connectionLog = ConnectionLog(
            id: connectionIdCounter,
            timestamp: getCurrentTimestamp(),
            connectionProtocol: proto,
            process: process,
            destination: dest,
            port: port,
            proxy: proxy
        )
        connections.append(connectionLog)
        
        // Trim in bulk to avoid O(n) shift on every entry at the limit
        if connections.count > maxLogEntries {
            connections.removeFirst(connections.count - trimToEntries)
        }
    }
    
    private func handleActivityLog(_ log: [String: String]) {
        guard let timestamp = log["timestamp"],
              let level = log["level"],
              let message = log["message"] else {
            return
        }
        
        activityIdCounter &+= 1
        let activityLog = ActivityLog(
            id: activityIdCounter,
            timestamp: timestamp,
            level: level,
            message: message
        )
        activityLogs.append(activityLog)
        
        if activityLogs.count > maxLogEntries {
            activityLogs.removeFirst(activityLogs.count - trimToEntries)
        }
    }
    
    func setProxyConfig(_ config: ProxyConfig) {
        proxyEngine = .external
        proxyConfig = config
        saveProxyConfig(config)
        saveProxyEngineConfig()
        
        guard let session = tunnelSession else {
            addLog("ERROR", "Extension not connected")
            return
        }
        
        sendProxyConfigToExtension(config, session: session)
    }

    func setBuiltInProxyConfig(_ config: BuiltInProxyConfig) {
        proxyEngine = .builtIn
        builtInProxyConfig = config
        saveProxyEngineConfig()

        guard let session = tunnelSession else {
            addLog("INFO", "Built-in proxy settings saved")
            return
        }

        startBuiltInProxy { [weak self] success in
            guard let self = self, success else { return }
            let localConfig = ProxyConfig(
                type: "socks5",
                host: "127.0.0.1",
                port: self.builtInProxyConfig.mixedPort,
                username: nil,
                password: nil
            )
            self.sendProxyConfigToExtension(localConfig, session: session)
        }
    }

    func saveBuiltInProxySettings(_ config: BuiltInProxyConfig) {
        proxyEngine = .builtIn
        builtInProxyConfig = config
        saveProxyEngineConfig()
        addLog("INFO", "Built-in proxy settings saved")
    }

    func refreshBuiltInProfile(completion: @escaping (Bool, String) -> Void) {
        ensurePortableFolders()
        let profileURL = resolvePortableURL(builtInProxyConfig.activeProfilePath)

        if !builtInProxyConfig.subscriptionUrl.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
           let url = URL(string: builtInProxyConfig.subscriptionUrl) {
            URLSession.shared.dataTask(with: url) { data, _, error in
                if let error = error {
                    DispatchQueue.main.async { completion(false, error.localizedDescription) }
                    return
                }

                guard let data = data else {
                    DispatchQueue.main.async { completion(false, "No subscription data received") }
                    return
                }

                do {
                    try data.write(to: profileURL, options: .atomic)
                    DispatchQueue.main.async { completion(true, "Profile updated: \(profileURL.path)") }
                } catch {
                    DispatchQueue.main.async { completion(false, error.localizedDescription) }
                }
            }.resume()
            return
        }

        if !builtInProxyConfig.localYamlPath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            let sourceURL = resolvePortableURL(builtInProxyConfig.localYamlPath)
            do {
                if FileManager.default.fileExists(atPath: profileURL.path) {
                    try FileManager.default.removeItem(at: profileURL)
                }
                try FileManager.default.copyItem(at: sourceURL, to: profileURL)
                completion(true, "Profile copied: \(profileURL.path)")
            } catch {
                completion(false, error.localizedDescription)
            }
            return
        }

        completion(false, "Add a subscription URL or local YAML profile")
    }

    private func startBuiltInProxy(completion: @escaping (Bool) -> Void) {
        if let process = mihomoProcess, process.isRunning {
            completion(true)
            return
        }

        ensurePortableFolders()

        if builtInProxyConfig.controllerSecret.isEmpty {
            builtInProxyConfig.controllerSecret = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased()
            saveProxyEngineConfig()
        }

        let coreURL = resolvePortableURL(builtInProxyConfig.corePath)
        guard FileManager.default.fileExists(atPath: coreURL.path) else {
            addLog("ERROR", "mihomo core not found: \(coreURL.path)")
            completion(false)
            return
        }

        let profileURL = resolvePortableURL(builtInProxyConfig.activeProfilePath)
        let launch: () -> Void = { [weak self] in
            guard let self = self else { return }

            do {
                try self.writeRuntimeMihomoConfig(profileURL: profileURL)

                let runtimeURL = self.resolvePortableURL("core/runtime.yaml")
                let process = Process()
                process.executableURL = coreURL
                process.arguments = ["-f", runtimeURL.path, "-d", self.resolvePortableURL("data").path]
                process.currentDirectoryURL = coreURL.deletingLastPathComponent()
                process.terminationHandler = { [weak self] _ in
                    DispatchQueue.main.async {
                        self?.addLog("INFO", "Built-in proxy core stopped")
                    }
                }

                try process.run()
                self.mihomoProcess = process
                self.addLog("INFO", "Built-in proxy core started on 127.0.0.1:\(self.builtInProxyConfig.mixedPort)")
                completion(true)
            } catch {
                self.addLog("ERROR", "Failed to start built-in proxy: \(error.localizedDescription)")
                completion(false)
            }
        }

        if FileManager.default.fileExists(atPath: profileURL.path) {
            launch()
        } else {
            refreshBuiltInProfile { [weak self] success, message in
                self?.addLog(success ? "INFO" : "ERROR", message)
                if success { launch() } else { completion(false) }
            }
        }
    }

    private func stopBuiltInProxy() {
        if let process = mihomoProcess, process.isRunning {
            process.terminate()
        }
        mihomoProcess = nil
    }

    private func writeRuntimeMihomoConfig(profileURL: URL) throws {
        let profile = try String(contentsOf: profileURL)
        let suffix = """

mixed-port: \(builtInProxyConfig.mixedPort)
allow-lan: false
external-controller: 127.0.0.1:\(builtInProxyConfig.controllerPort)
secret: "\(builtInProxyConfig.controllerSecret)"
"""
        try (profile.trimmingCharacters(in: .whitespacesAndNewlines) + suffix)
            .write(to: resolvePortableURL("core/runtime.yaml"), atomically: true, encoding: .utf8)
    }

    private func ensurePortableFolders() {
        ["core", "profiles", "data", "rules", "logs"].forEach {
            try? FileManager.default.createDirectory(at: resolvePortableURL($0), withIntermediateDirectories: true)
        }
    }

    private func resolvePortableURL(_ path: String) -> URL {
        let expanded = NSString(string: path).expandingTildeInPath
        if expanded.hasPrefix("/") {
            return URL(fileURLWithPath: expanded)
        }

        let appContainer = Bundle.main.bundleURL.deletingLastPathComponent()
        return appContainer.appendingPathComponent(expanded)
    }
    
    private func sendProxyConfigToExtension(_ config: ProxyConfig, session: NETunnelProviderSession) {
        var message: [String: Any] = [
            "action": "setProxyConfig",
            "proxyType": config.type,
            "proxyHost": config.host,
            "proxyPort": config.port
        ]
        
        if let username = config.username {
            message["proxyUsername"] = username
        }
        if let password = config.password {
            message["proxyPassword"] = password
        }
        
        guard let data = try? JSONSerialization.data(withJSONObject: message) else {
            addLog("ERROR", "Failed to encode proxy config")
            return
        }
        
        try? session.sendProviderMessage(data) { [weak self] response in
            if let responseData = response,
               let json = try? JSONSerialization.jsonObject(with: responseData) as? [String: Any],
               let status = json["status"] as? String, status == "ok" {
                DispatchQueue.main.async {
                    self?.addLog("INFO", "Proxy configured: \(config.type)://\(config.host):\(config.port)")
                }
            }
        }
    }
    
    func clearConnections() {
        connections.removeAll()
    }
    
    func clearActivityLogs() {
        activityLogs.removeAll()
    }
    
    private func addLog(_ level: String, _ message: String) {
        activityIdCounter &+= 1
        let log = ActivityLog(
            id: activityIdCounter,
            timestamp: getCurrentTimestamp(),
            level: level,
            message: message
        )
        activityLogs.append(log)
        
        if activityLogs.count > maxLogEntries {
            activityLogs.removeFirst(activityLogs.count - trimToEntries)
        }
    }
    
    private func getCurrentTimestamp() -> String {
        return timestampFormatter.string(from: Date())
    }
    
    deinit {
        logTimer?.invalidate()
        stopProxy()
    }
}

extension JackBridgeViewModel: OSSystemExtensionRequestDelegate {
    func request(_ request: OSSystemExtensionRequest, didFinishWithResult result: OSSystemExtensionRequest.Result) {
        DispatchQueue.main.async {
            self.addLog("INFO", "Extension installed successfully")
            self.startProxy()
        }
    }
    
    func request(_ request: OSSystemExtensionRequest, didFailWithError error: Error) {
        DispatchQueue.main.async {
            self.addLog("ERROR", "Extension failed: \(error.localizedDescription)")
        }
    }
    
    func requestNeedsUserApproval(_ request: OSSystemExtensionRequest) {
        DispatchQueue.main.async {
            self.addLog("INFO", "Extension needs user approval in System Settings")
        }
    }
    
    func request(_ request: OSSystemExtensionRequest, actionForReplacingExtension existing: OSSystemExtensionProperties, withExtension ext: OSSystemExtensionProperties) -> OSSystemExtensionRequest.ReplacementAction {
        print("Replacing existing extension")
        return .replace
    }
}
