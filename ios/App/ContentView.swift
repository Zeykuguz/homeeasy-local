import SwiftUI
import HomeEasyKit

@MainActor
final class ACViewModel: ObservableObject {
    @Published var host: String = UserDefaults.standard.string(forKey: "host") ?? "192.168.0.5"
    @Published var state: ACState?
    @Published var message: String = ""
    @Published var busy = false
    @Published var scanProgress: Int?
    @Published var timerMinutes = 60

    private var pollTask: Task<Void, Never>?

    var client: ACClient { ACClient(host: host.trimmingCharacters(in: .whitespaces)) }

    func start() {
        pollTask?.cancel()
        pollTask = Task { [weak self] in
            while !Task.isCancelled {
                await self?.refresh(quiet: true)
                try? await Task.sleep(nanoseconds: 5_000_000_000)
            }
        }
    }

    func stop() {
        pollTask?.cancel()
        pollTask = nil
    }

    func connect() {
        UserDefaults.standard.set(host, forKey: "host")
        state = nil
        start()
    }

    func refresh(quiet: Bool = false) async {
        if busy { return }
        do {
            state = try await client.status()
            message = ""
        } catch {
            if !quiet || state == nil { message = error.localizedDescription }
        }
    }

    func change(_ apply: @escaping (inout ACState) -> Void) {
        Task {
            busy = true
            defer { busy = false }
            do {
                state = try await client.update(apply)
                message = ""
            } catch {
                message = error.localizedDescription
            }
        }
    }

    func binding<T>(_ path: WritableKeyPath<ACState, T>, default value: T) -> Binding<T> {
        Binding(
            get: { self.state?[keyPath: path] ?? value },
            set: { newValue in
                self.state?[keyPath: path] = newValue
                self.change { $0[keyPath: path] = newValue }
            })
    }

    func scan() {
        Task {
            scanProgress = 0
            message = "Scanning the Wi-Fi network…"
            let found = await ACScanner.scan { p in Task { @MainActor in self.scanProgress = p } }
            scanProgress = nil
            if let first = found.first {
                host = first
                message = found.count > 1 ? "Found: \(found.joined(separator: ", "))" : ""
                connect()
            } else {
                message = "No air conditioner found. Check that the iPhone is on the home Wi-Fi and Local Network access is allowed (Settings → Privacy & Security → Local Network)."
            }
        }
    }
}

struct ContentView: View {
    @StateObject private var vm = ACViewModel()
    @Environment(\.scenePhase) private var scenePhase

    private let presets = [15, 30, 60, 90, 120, 180, 240, 360, 480, 720]

    var body: some View {
        NavigationView {
            Form {
                Section("Air conditioner") {
                    HStack {
                        TextField("IP address", text: $vm.host)
                            .keyboardType(.decimalPad)
                            .textInputAutocapitalization(.never)
                            .disableAutocorrection(true)
                        Button("Connect") { vm.connect() }
                    }
                    Button(vm.scanProgress == nil ? "Scan network" : "Scanning… \(vm.scanProgress!)%") { vm.scan() }
                        .disabled(vm.scanProgress != nil)
                    if !vm.message.isEmpty {
                        Text(vm.message).font(.footnote).foregroundColor(.red)
                    }
                }

                if let s = vm.state {
                    Section {
                        HStack {
                            Text("Indoor")
                            Spacer()
                            Text(String(format: "%.1f °C", s.indoor)).font(.title2.monospacedDigit())
                        }
                        Toggle("Power", isOn: vm.binding(\.power, default: false))
                            .font(.headline)
                        Stepper(value: vm.binding(\.setpoint, default: 24), in: 16...30) {
                            HStack {
                                Text("Target")
                                Spacer()
                                Text("\(s.setpoint) °C").font(.title2.monospacedDigit())
                            }
                        }
                        Picker("Mode", selection: vm.binding(\.mode, default: .auto)) {
                            ForEach(ACMode.allCases) { Text($0.title).tag($0) }
                        }
                        .pickerStyle(.segmented)
                    }

                    Section("Fan & swing") {
                        Picker("Fan", selection: vm.binding(\.fan, default: .auto)) {
                            ForEach(FanSetting.allCases) { Text($0.title).tag($0) }
                        }
                        Picker("Vertical swing", selection: vm.binding(\.verticalSwing, default: .stop)) {
                            ForEach(VerticalSwing.allCases) { Text($0.title).tag($0) }
                        }
                        Picker("Horizontal swing", selection: vm.binding(\.horizontalSwing, default: .stop)) {
                            ForEach(HorizontalSwing.allCases) { Text($0.title).tag($0) }
                        }
                    }

                    Section("Options") {
                        Toggle("Eco", isOn: vm.binding(\.eco, default: false))
                        Toggle("Sleep", isOn: vm.binding(\.sleep, default: false))
                        Toggle("Health", isOn: vm.binding(\.health, default: false))
                        Toggle("Aux heat / drying", isOn: vm.binding(\.aux, default: false))
                        Toggle("Display light", isOn: vm.binding(\.display, default: true))
                    }

                    Section("Off timer") {
                        if s.offTimerEnabled && s.offTimerMinutes > 0 {
                            HStack {
                                Text("Turns off in")
                                Spacer()
                                Text(Self.format(s.offTimerMinutes)).monospacedDigit()
                            }
                            Button("Cancel timer", role: .destructive) { vm.change { $0.setOffTimer(minutes: nil) } }
                        } else {
                            Picker("Turn off after", selection: $vm.timerMinutes) {
                                ForEach(presets, id: \.self) { Text(Self.format($0)).tag($0) }
                            }
                            Button("Start timer") {
                                let m = vm.timerMinutes
                                vm.change { $0.setOffTimer(minutes: m) }
                            }
                            .disabled(!s.power)
                        }
                    }
                }
            }
            .navigationTitle("AC Control")
            .refreshable { await vm.refresh() }
        }
        .navigationViewStyle(.stack)
        .onChange(of: scenePhase) { phase in
            if phase == .active { vm.start() } else { vm.stop() }
        }
        .onAppear { vm.start() }
    }

    static func format(_ minutes: Int) -> String {
        let h = minutes / 60, m = minutes % 60
        if h == 0 { return "\(m) min" }
        return m == 0 ? "\(h) h" : "\(h) h \(m) min"
    }
}
