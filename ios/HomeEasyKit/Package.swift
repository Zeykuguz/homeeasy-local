// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "HomeEasyKit",
    platforms: [.iOS(.v15), .macOS(.v12)],
    products: [.library(name: "HomeEasyKit", targets: ["HomeEasyKit"])],
    targets: [
        .target(name: "HomeEasyKit"),
        .testTarget(name: "HomeEasyKitTests", dependencies: ["HomeEasyKit"]),
    ]
)
