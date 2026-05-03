import SwiftUI

struct ProxySettingsView: View {
    @ObservedObject var viewModel: JackBridgeViewModel
    @Environment(\.dismiss) private var dismiss
    
    @State private var proxyEngine = JackBridgeViewModel.ProxyEngine.external
    @State private var proxyType = ""
    @State private var proxyHost = ""
    @State private var proxyPort = ""
    @State private var username = ""
    @State private var password = ""
    @State private var corePath = ""
    @State private var subscriptionUrl = ""
    @State private var localYamlPath = ""
    @State private var activeProfilePath = ""
    @State private var mixedPort = ""
    @State private var controllerPort = ""
    @State private var autoUpdateSubscription = false
    @State private var validationError = ""
    @State private var profileStatus = ""
    @State private var isRefreshingProfile = false
    
    private let proxyTypes = ["http", "socks5"]
    private var isSaveDisabled: Bool {
        if !validationError.isEmpty { return true }
        if proxyEngine == .builtIn {
            return mixedPort.isEmpty || controllerPort.isEmpty || (subscriptionUrl.isEmpty && localYamlPath.isEmpty)
        }
        return proxyType.isEmpty || proxyHost.isEmpty || proxyPort.isEmpty
    }
    
    var body: some View {
        VStack(spacing: 0) {
            headerView
            formContent
            Divider()
            footerButtons
        }
        .frame(width: 680, height: 760)
        .onAppear(perform: loadCurrentSettings)
    }
    
    private var headerView: some View {
        HStack {
            Image(systemName: "network")
                .font(.title2)
                .foregroundColor(.accentColor)
            Text("Proxy Settings")
                .font(.title2)
                .fontWeight(.semibold)
            Spacer()
        }
        .padding()
        .background(Color(NSColor.controlBackgroundColor))
    }
    
    private var formContent: some View {
        Form {
            Section {
                Picker("Proxy Engine", selection: $proxyEngine) {
                    Text("External Proxy").tag(JackBridgeViewModel.ProxyEngine.external)
                    Text("Built-in Proxy").tag(JackBridgeViewModel.ProxyEngine.builtIn)
                }
                .pickerStyle(.segmented)
                .onChange(of: proxyEngine) { _ in validateInputs() }
            }

            Section {
                if proxyEngine == .external {
                    formPicker(label: "Proxy Type", selection: $proxyType, required: true)
                    formTextField(label: "Proxy IP/Domain", placeholder: "127.0.0.1 or proxy.example.com", text: $proxyHost, required: true)
                        .onChange(of: proxyHost) { _ in validateInputs() }
                    formTextField(label: "Proxy Port", placeholder: "8080", text: $proxyPort, required: true)
                        .onChange(of: proxyPort) { _ in validateInputs() }
                    formTextField(label: "Username", placeholder: "Leave empty if no auth required", text: $username)
                    formSecureField(label: "Password", placeholder: "Leave empty if no auth required", text: $password)
                } else {
                    formTextField(label: "Core Path", placeholder: "core/mihomo", text: $corePath, required: true)
                    formTextField(label: "Subscription URL", placeholder: "https://example.com/subscription", text: $subscriptionUrl)
                        .onChange(of: subscriptionUrl) { _ in validateInputs() }
                    formTextField(label: "Local YAML Path", placeholder: "profiles/custom.yaml or /path/config.yaml", text: $localYamlPath)
                        .onChange(of: localYamlPath) { _ in validateInputs() }
                    formTextField(label: "Active Profile", placeholder: "profiles/mihomo.yaml", text: $activeProfilePath, required: true)
                    formTextField(label: "Mixed Port", placeholder: "7892", text: $mixedPort, required: true)
                        .onChange(of: mixedPort) { _ in validateInputs() }
                    formTextField(label: "Controller Port", placeholder: "9090", text: $controllerPort, required: true)
                        .onChange(of: controllerPort) { _ in validateInputs() }
                    Toggle("Auto update subscription", isOn: $autoUpdateSubscription)

                    HStack {
                        Button(isRefreshingProfile ? "Updating..." : "Update Profile Now") {
                            refreshProfile()
                        }
                        .disabled(isRefreshingProfile)

                        if !profileStatus.isEmpty {
                            Text(profileStatus)
                                .font(.caption)
                                .foregroundColor(profileStatus.hasPrefix("ERROR") ? .red : .secondary)
                                .lineLimit(2)
                        }
                    }
                    .padding(.top, 4)
                }
                
                if !validationError.isEmpty {
                    HStack {
                        Image(systemName: "exclamationmark.triangle.fill")
                            .foregroundColor(.red)
                        Text(validationError)
                            .font(.caption)
                            .foregroundColor(.red)
                    }
                    .padding(.top, 4)
                }
                
                HStack {
                    Text("* Required fields")
                        .font(.caption)
                        .foregroundColor(.secondary)
                    Spacer()
                }
                .padding(.top, 4)
            }
        }
        .formStyle(.grouped)
    }
    
    private var footerButtons: some View {
        HStack(spacing: 12) {
            Spacer()
            Button("Cancel") {
                dismiss()
            }
            .keyboardShortcut(.cancelAction)
            
            Button("Save Changes") {
                saveSettings()
                dismiss()
            }
            .buttonStyle(.borderedProminent)
            .keyboardShortcut(.defaultAction)
            .disabled(isSaveDisabled)
        }
        .padding()
        .background(Color(NSColor.controlBackgroundColor))
    }
    
    @ViewBuilder
    private func formPicker(label: String, selection: Binding<String>, required: Bool = false) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            labelWithRequiredMark(label: label, required: required)
            Picker("Select proxy type", selection: selection) {
                Text("Select proxy type").tag("")
                ForEach(proxyTypes, id: \.self) { type in
                    Text(type.uppercased()).tag(type)
                }
            }
            .pickerStyle(.menu)
        }
        .padding(.vertical, 8)
    }
    
    @ViewBuilder
    private func formTextField(label: String, placeholder: String, text: Binding<String>, required: Bool = false) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            labelWithRequiredMark(label: label, required: required)
            TextField(placeholder, text: text)
                .textFieldStyle(.roundedBorder)
        }
        .padding(.vertical, 8)
    }
    
    @ViewBuilder
    private func formSecureField(label: String, placeholder: String, text: Binding<String>, required: Bool = false) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            labelWithRequiredMark(label: label, required: required)
            SecureField(placeholder, text: text)
                .textFieldStyle(.roundedBorder)
        }
        .padding(.vertical, 8)
    }
    
    @ViewBuilder
    private func labelWithRequiredMark(label: String, required: Bool) -> some View {
        HStack {
            Text(label)
                .fontWeight(.medium)
            if required {
                Text("*")
                    .foregroundColor(.red)
            }
        }
    }
    
    private func loadCurrentSettings() {
        proxyEngine = viewModel.proxyEngine

        if let config = viewModel.proxyConfig {
            proxyType = config.type
            proxyHost = config.host
            proxyPort = String(config.port)
            username = config.username ?? ""
            password = config.password ?? ""
        }

        let builtIn = viewModel.builtInProxyConfig
        corePath = builtIn.corePath
        subscriptionUrl = builtIn.subscriptionUrl
        localYamlPath = builtIn.localYamlPath
        activeProfilePath = builtIn.activeProfilePath
        mixedPort = String(builtIn.mixedPort)
        controllerPort = String(builtIn.controllerPort)
        autoUpdateSubscription = builtIn.autoUpdateSubscription
        validateInputs()
    }
    
    private func validateInputs() {
        if proxyEngine == .builtIn {
            if subscriptionUrl.isEmpty && localYamlPath.isEmpty {
                validationError = "Add a subscription URL or local YAML profile"
                return
            }

            if !mixedPort.isEmpty {
                if let port = Int(mixedPort) {
                    if port < 1 || port > 65535 {
                        validationError = "Mixed port must be between 1 and 65535"
                        return
                    }
                } else {
                    validationError = "Mixed port must be a valid number"
                    return
                }
            }

            if !controllerPort.isEmpty {
                if let port = Int(controllerPort) {
                    if port < 1 || port > 65535 {
                        validationError = "Controller port must be between 1 and 65535"
                        return
                    }
                } else {
                    validationError = "Controller port must be a valid number"
                    return
                }
            }

            validationError = ""
            return
        }

        if !proxyHost.isEmpty && !isValidHost(proxyHost) {
            validationError = "Invalid proxy IP/domain"
            return
        }
        
        if !proxyPort.isEmpty {
            if let port = Int(proxyPort) {
                if port < 1 || port > 65535 {
                    validationError = "Port must be between 1 and 65535"
                    return
                }
            } else {
                validationError = "Port must be a valid number"
                return
            }
        }
        
        validationError = ""
    }
    
    private func isValidHost(_ host: String) -> Bool {
        if isValidIPv4(host) {
            return true
        }
        
        if isValidIPv6(host) {
            return true
        }
        
        if isValidDomain(host) {
            return true
        }
        
        return false
    }
    
    private func isValidIPv4(_ ip: String) -> Bool {
        let parts = ip.split(separator: ".")
        guard parts.count == 4 else { return false }
        
        for part in parts {
            guard let num = Int(part), num >= 0 && num <= 255 else {
                return false
            }
        }
        return true
    }
    
    private func isValidIPv6(_ ip: String) -> Bool {
        let validChars = CharacterSet(charactersIn: "0123456789abcdefABCDEF:")
        return ip.rangeOfCharacter(from: validChars.inverted) == nil && ip.contains(":")
    }
    
    private func isValidDomain(_ domain: String) -> Bool {
        let domainPattern = "^([a-zA-Z0-9]([a-zA-Z0-9\\-]{0,61}[a-zA-Z0-9])?\\.)*[a-zA-Z0-9]([a-zA-Z0-9\\-]{0,61}[a-zA-Z0-9])?$"
        let predicate = NSPredicate(format: "SELF MATCHES %@", domainPattern)
        return predicate.evaluate(with: domain) || domain == "localhost"
    }
    
    private func saveSettings() {
        if proxyEngine == .builtIn {
            guard let config = makeBuiltInConfig() else { return }
            viewModel.setBuiltInProxyConfig(config)
        } else {
            guard let port = Int(proxyPort) else { return }

            let config = JackBridgeViewModel.ProxyConfig(
                type: proxyType,
                host: proxyHost,
                port: port,
                username: username.isEmpty ? nil : username,
                password: password.isEmpty ? nil : password
            )

            viewModel.setProxyConfig(config)
        }
    }

    private func refreshProfile() {
        guard let config = makeBuiltInConfig() else { return }
        viewModel.saveBuiltInProxySettings(config)
        isRefreshingProfile = true
        profileStatus = "Updating profile..."
        viewModel.refreshBuiltInProfile { success, message in
            isRefreshingProfile = false
            profileStatus = success ? message : "ERROR: \(message)"
        }
    }

    private func makeBuiltInConfig() -> JackBridgeViewModel.BuiltInProxyConfig? {
        guard let mixed = Int(mixedPort),
              let controller = Int(controllerPort) else { return nil }

        var config = viewModel.builtInProxyConfig
        config.corePath = corePath.isEmpty ? "core/mihomo" : corePath
        config.subscriptionUrl = subscriptionUrl
        config.localYamlPath = localYamlPath
        config.activeProfilePath = activeProfilePath.isEmpty ? "profiles/mihomo.yaml" : activeProfilePath
        config.mixedPort = mixed
        config.controllerPort = controller
        config.autoUpdateSubscription = autoUpdateSubscription
        return config
    }
}
