// swift-tools-version: 5.9
import PackageDescription
let package = Package(name: "LiteLink", platforms: [.macOS("15.0")],
    products: [.executable(name: "LiteLink", targets: ["LiteLink"])],
    targets: [.executableTarget(name: "LiteLink"), .testTarget(name: "LiteLinkTests", dependencies: ["LiteLink"])])
