import Foundation
import Network

public enum ACError: LocalizedError {
    case timeout, closed, badFrame, badAddress

    public var errorDescription: String? {
        switch self {
        case .timeout: return "The air conditioner did not answer. Is the iPhone on the same home Wi-Fi?"
        case .closed: return "Connection closed by the air conditioner."
        case .badFrame: return "Unexpected reply from the air conditioner."
        case .badAddress: return "Invalid IP address."
        }
    }
}

/// Talks directly to the AC on TCP 12416 (one short connection per request).
public final class ACClient {
    public static let controlPort: UInt16 = 12416
    public let host: String
    public var timeout: TimeInterval = 4

    public init(host: String) {
        self.host = host
    }

    public func status() async throws -> ACState {
        try await request(ACState.statusRequest)
    }

    public func send(_ state: ACState) async throws -> ACState {
        try await request(state.command)
    }

    /// Reads the current state, applies `change`, sends it and returns the AC's new state.
    public func update(_ change: (inout ACState) -> Void) async throws -> ACState {
        var state = try await status()
        change(&state)
        return try await send(state)
    }

    private func request(_ bytes: [UInt8]) async throws -> ACState {
        let reply = try await Self.exchange(host: host, payload: bytes, timeout: timeout)
        guard let state = ACState(frame: reply) else { throw ACError.badFrame }
        return state
    }

    static func extractFrame(_ buffer: [UInt8]) -> [UInt8]? {
        guard buffer.count >= ACState.frameLength else { return nil }
        for i in 0...(buffer.count - ACState.frameLength)
        where buffer[i] == 0xAA && buffer[i + 1] == 0xAA && buffer[i + 2] == 0x12 {
            return Array(buffer[i..<(i + ACState.frameLength)])
        }
        return nil
    }

    static func exchange(host: String, payload: [UInt8], timeout: TimeInterval) async throws -> [UInt8] {
        guard let port = NWEndpoint.Port(rawValue: controlPort), !host.isEmpty else { throw ACError.badAddress }
        return try await withCheckedThrowingContinuation { continuation in
            let connection = NWConnection(host: NWEndpoint.Host(host), port: port, using: .tcp)
            let queue = DispatchQueue(label: "ac.connection")
            var finished = false
            var buffer: [UInt8] = []

            // All callbacks run on `queue`, so the captured state is not accessed concurrently.
            func finish(_ result: Result<[UInt8], Error>) {
                guard !finished else { return }
                finished = true
                connection.cancel()
                continuation.resume(with: result)
            }

            func receive() {
                connection.receive(minimumIncompleteLength: 1, maximumLength: 256) { data, _, isComplete, error in
                    if let data = data { buffer.append(contentsOf: data) }
                    if let frame = extractFrame(buffer) { finish(.success(frame)); return }
                    if let error = error { finish(.failure(error)); return }
                    if isComplete { finish(.failure(ACError.closed)); return }
                    receive()
                }
            }

            connection.stateUpdateHandler = { state in
                switch state {
                case .ready:
                    connection.send(content: Data(payload), completion: .contentProcessed { error in
                        if let error = error { finish(.failure(error)) }
                    })
                    receive()
                case .failed(let error):
                    finish(.failure(error))
                default:
                    break // .waiting happens while iOS asks for Local Network permission; rely on the timeout.
                }
            }
            connection.start(queue: queue)
            queue.asyncAfter(deadline: .now() + timeout) { finish(.failure(ACError.timeout)) }
        }
    }
}

/// Finds ACs by probing every address of the phone's Wi-Fi /24 subnet on TCP 12416.
/// (UDP broadcast discovery needs a special Apple entitlement, so a unicast scan is used instead.)
public enum ACScanner {
    public static func wifiIPv4Address() -> String? {
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let first = ifaddr else { return nil }
        defer { freeifaddrs(ifaddr) }
        var fallback: String?
        for ptr in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let ifa = ptr.pointee
            guard let addr = ifa.ifa_addr, addr.pointee.sa_family == UInt8(AF_INET) else { continue }
            let name = String(cString: ifa.ifa_name)
            guard name.hasPrefix("en") else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            guard getnameinfo(addr, socklen_t(addr.pointee.sa_len), &host, socklen_t(host.count),
                              nil, 0, NI_NUMERICHOST) == 0 else { continue }
            let ip = String(cString: host)
            if ip.hasPrefix("169.254.") { continue }
            if name == "en0" { return ip }
            fallback = fallback ?? ip
        }
        return fallback
    }

    public static func scan(progress: ((Int) -> Void)? = nil) async -> [String] {
        guard let ip = wifiIPv4Address() else { return [] }
        let parts = ip.split(separator: ".")
        guard parts.count == 4 else { return [] }
        let prefix = parts.prefix(3).joined(separator: ".")
        let hosts = (1...254).map { "\(prefix).\($0)" }.filter { $0 != ip }
        var found: [String] = []
        var done = 0
        let chunks = stride(from: 0, to: hosts.count, by: 32).map { Array(hosts[$0..<min($0 + 32, hosts.count)]) }
        for chunk in chunks {
            let hits = await withTaskGroup(of: String?.self, returning: [String].self) { group in
                for host in chunk {
                    group.addTask {
                        guard let reply = try? await ACClient.exchange(host: host, payload: ACState.statusRequest, timeout: 1.5),
                              ACState(frame: reply) != nil else { return nil }
                        return host
                    }
                }
                var result: [String] = []
                for await host in group { if let host = host { result.append(host) } }
                return result
            }
            found.append(contentsOf: hits)
            done += chunk.count
            progress?(done * 100 / hosts.count)
        }
        return found.sorted { lastOctet($0) < lastOctet($1) }
    }

    private static func lastOctet(_ ip: String) -> Int {
        Int(ip.split(separator: ".").last ?? "") ?? 0
    }
}
