// 生成 64x64 ⚡ 图标 → 经典 BMP-DIB 格式 ICO（csc 非平台资源写入器与 GDI 都认）
// 用法: swift scripts/make-icon.swift <输出.ico 路径>
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "app.ico"
let W = 64, H = 64

guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H,
                                 bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                 colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else {
    fatalError("创建位图失败")
}
rep.size = NSSize(width: W, height: H)
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

NSColor.clear.setFill()
NSRect(x: 0, y: 0, width: W, height: H).fill()
let s = CGFloat(W)
NSColor.systemBlue.setFill()
NSBezierPath(roundedRect: NSRect(x: 2, y: 2, width: s - 4, height: s - 4), xRadius: s * 0.22, yRadius: s * 0.22).fill()
NSColor.white.setFill()
let bolt = NSBezierPath()
bolt.move(to: NSPoint(x: 0.62 * s, y: 0.86 * s))
bolt.line(to: NSPoint(x: 0.28 * s, y: 0.48 * s))
bolt.line(to: NSPoint(x: 0.47 * s, y: 0.48 * s))
bolt.line(to: NSPoint(x: 0.38 * s, y: 0.14 * s))
bolt.line(to: NSPoint(x: 0.74 * s, y: 0.54 * s))
bolt.line(to: NSPoint(x: 0.54 * s, y: 0.54 * s))
bolt.close()
bolt.fill()
NSGraphicsContext.restoreGraphicsState()

// XOR 位图（自底向上，BGRA）
var px = [UInt8](repeating: 0, count: W * H * 4)
// AND 掩码（1=透明；64 位/行 = 8 字节，天然 4 字节对齐）
var mask = [UInt8](repeating: 0, count: H * 8)

for y in 0..<H {
    for x in 0..<W {
        var comps = [Int](repeating: 0, count: 5)
        rep.getPixel(&comps, atX: x, y: y) // [r, g, b, a]（0-255）
        let b = comps[2], g = comps[1], r = comps[0], a = comps[3]
        let dstRow = H - 1 - y
        let off = (dstRow * W + x) * 4
        px[off + 0] = UInt8(b)
        px[off + 1] = UInt8(g)
        px[off + 2] = UInt8(r)
        px[off + 3] = UInt8(a)
        if a < 128 {
            mask[dstRow * 8 + x / 8] |= (1 << (7 - x % 8))
        }
    }
}

func le32(_ v: UInt32) -> [UInt8] { [UInt8(v & 0xff), UInt8((v >> 8) & 0xff), UInt8((v >> 16) & 0xff), UInt8((v >> 24) & 0xff)] }
func le16(_ v: UInt16) -> [UInt8] { [UInt8(v & 0xff), UInt8((v >> 8) & 0xff)] }

let xorSize = px.count            // 64*64*4 = 16384
let andSize = mask.count          // 64*8 = 512
let dibSize = 40 + xorSize + andSize

var ico = [UInt8]()
ico += [0, 0]                     // reserved
ico += [1, 0]                     // type = icon
ico += [1, 0]                     // count
ico += [UInt8(W), UInt8(H), 0, 0] // w/h/colors/reserved
ico += le16(1)                    // planes
ico += le16(32)                   // bitcount
ico += le32(UInt32(dibSize))      // bytes in resource
ico += le32(22)                   // data offset (6+16)
// BITMAPINFOHEADER
ico += le32(40)                   // biSize
ico += le32(UInt32(W))            // biWidth
ico += le32(UInt32(H * 2))        // biHeight = XOR + AND
ico += le16(1)                    // biPlanes
ico += le16(32)                   // biBitCount
ico += le32(0)                    // biCompression = BI_RGB
ico += le32(UInt32(xorSize + andSize))
ico += [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] // 分辨率/颜色使用全 0
ico += px
ico += mask

try Data(ico).write(to: URL(fileURLWithPath: out))
print("icon: \(out) (\(ico.count) bytes)")
