<p align="center">
  <img src="assets/Garden-Launcher-Icon.png" alt="Garden Launcher" width="116" />
</p>

<h1 align="center">Garden Launcher (iOS) — v7.10</h1>

<p align="center">
  Trình khởi chạy &amp; quản lý Mod SMAPI, điều khiển cảm ứng kiểu Mobile, Save Menu nhiều mốc thời gian &amp; hỗ trợ Shader cho Stardew Valley PC 1.6.15 trên iPhone/iPad (iOS 14 – iOS 27+, 0% JIT).<br/><br/>
  <a href="https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/latest"><b>⬇️ Tải Garden-Launcher-v7.10-iOS14-18.ipa (Cho iOS 14 – iOS 18)</b></a><br/>
  <a href="https://github.com/hanprm9-dot/Garden-Launcher-iOS/releases/latest"><b>⬇️ Tải Garden-Launcher-v7.10-iOS26-27.ipa (Cho iOS 26 – iOS 27+)</b></a>
</p>

> **Lưu ý bản quyền:** Kho mã nguồn và các tệp `.ipa` phát hành tại đây là **Launcher sạch (0% tệp game bản quyền của ConcernedApe)**. Bạn cần tự cung cấp bộ dữ liệu **Stardew Valley PC 1.6.15** của riêng mình.

---

## 📦 Chọn đúng phiên bản IPA theo iOS của máy

| Phiên bản iOS trên máy | Tệp IPA cần cài | Kiến trúc vòng đời |
| :--- | :--- | :--- |
| **iOS 14.0 – iOS 18.x** | `Garden-Launcher-v7.10-iOS14-18.ipa` | Classic `UIApplicationDelegate` (ổn định tuyệt đối trên iOS 14–18) |
| **iOS 26.0 – iOS 27+** | `Garden-Launcher-v7.10-iOS26-27.ipa` | Chuẩn `UIScene` (`SDVSceneDelegate`) + `UIDesignRequiresCompatibility` |

---

## 📲 Hướng dẫn cài đặt

1. **Cài IPA:** Tải đúng bản `.ipa` theo bảng trên và cài bằng **KSign / ESign / TrollStore / SideStore / AltStore / Sideloadly** (có thể cài đè trực tiếp lên bản cũ nếu ký cùng chứng chỉ mà không mất Save hay Mod).
2. **Nạp dữ liệu PC `1.6.15` (chỉ làm 1 lần đầu):** Nén thư mục game **Stardew Valley PC 1.6.15** (`.ZIP` / `.RAR` / `.7Z`) → mở **Garden Launcher** → bấm **Nhập gói dữ liệu game** → chờ giải nén đủ 4 mục màu xanh → **vuốt tắt hẳn app rồi mở lại** để vào game.
3. **Cài Mod & Việt Hóa (Hỗ trợ `.ZIP`, `.RAR`, `.7Z`, `.TAR.GZ`, `.XNB` & Thư mục):**
   - Chạm **3 ngón tay 2 lần** (hoặc nút mở Mod Garden) → tab **Cài Mod**.
   - Bấm **Chọn tệp Mod (ZIP/RAR/7Z/...)** để cài trực tiếp mọi định dạng nén iPhone hỗ trợ, hoặc bấm **Chọn thư mục Mod** nếu bạn đã giải nén sẵn trong app *Tệp*.
4. **Điều khiển Mobile, Save Menu & Tùy chỉnh vị trí nút:**
   - Thanh tiện ích gọn phía trên trong game: **`BAG | MAP | QST | SAVE | KEY | EDIT | <`**.
   - **`SAVE`**: Mở bảng lưu/tải pixel phong cách Stardew Valley hỗ trợ **tới 20 mốc thời gian khác nhau** cho mỗi nhân vật (hiển thị Mùa/Ngày/Năm, Giờ trong game, Địa điểm/Tầng mỏ, Vàng, HP, Thể lực và giờ lưu thực tế).
   - **`EDIT`**: Kéo thả & chỉnh kích thước trực tiếp 2 vùng chạm ẩn, thanh tiện ích và các phím PC tùy chỉnh ngay trên màn hình.
   - **`KEY`**: Thêm nhanh các phím tắt PC (mở menu CJB Cheats, Lookup Anything, GMCM...) lên màn hình cảm ứng.

---

## ✨ Điểm mới nổi bật trong `v7.10`

- **Sửa triệt để âm thanh (OpenAL & `AVAudioSession`)**: Khắc phục lỗi `WaveBank.LoadSoundEffect` trên Mono Interpreter và tự động khôi phục phiên âm thanh iOS trên mọi phiên bản.
- **Giải nén đa định dạng trực tiếp (`/usr/lib/libarchive.2.dylib` + `miniz`)**: Đọc thẳng `.RAR` (RAR4/RAR5), `.7Z`, `.ZIP` (Deflate, Deflate64, LZMA, BZip2, Zstd), `.TAR.GZ`, `.TAR.XZ`, `.XNB` hoặc chọn trực tiếp thư mục đã giải nén; tự động bung gói nén lồng nhau và giải mã tên tệp tiếng Việt Windows (UTF-8 / CP1258 / CP1252).
- **Tương thích Mod Shader PC 1.6 & Phản chiếu bóng nhân vật (`DynamicReflections` / `DynamicShader`)**:
  - Bộ đọc kép tự động nhận diện bytecode `MGFX v9` (1-byte counts của *Dynamic Reflections*, *DynamicShader*...) lẫn `MGFX v10` (4-byte counts) và tự chuẩn hóa header `precision highp float;` cho GPU iOS OpenGL ES.
  - Sửa `LazyIosDisplayDevice.DrawTile` ủy quyền về `base.DrawTile` (`XnaDisplayDevice.DrawTile`) giúp Harmony patch của *Dynamic Reflections* lọc đúng ô nước, vũng nước và bầu trời sao để **hiển thị đầy đủ bóng phản chiếu của nhân vật và NPC dưới mặt nước**.
- **Hệ thống Cảnh báo Lưu Game 2 cấp độ khi RAM cao (`85%` & `90%`) + Tự động dọn RAM (`Auto-GC`)**:
  - **Mốc `85%` (Cảnh báo):** Tự động dọn RAM ngầm, hiện Banner cảnh báo màu cam góc trên + thông báo trong game và nhấp nháy cam/vàng nút **`SAVE!`** nhắc lưu game.
  - **Mốc `90%` (Cảnh báo mạnh khẩn cấp):** Hiện Banner đỏ đậm viền dày góc trên (`🚨 CẢNH BÁO MẠNH: RAM ĐẠT 90%+ ... SẮP TRÀN RAM VĂNG GAME!`) + hộp thoại đỏ trong game và nhấp nháy đỏ rực tốc độ cao nút **`SAVE!`** để người chơi kịp lưu game ngay lập tức.
- **Sửa cảm ứng bảng Nhiệm vụ (`QuestLog`) & Menu**: Giữ nút **`ESC`** góc trên-trái riêng cho bảng Nhiệm vụ để thoát nhanh, đồng thời chạm xem chi tiết nhiệm vụ, nhận thưởng, kéo thanh trượt Options và mua/bán nhiều đồ trong Shop bằng cách giữ lì.
- **Khóa thanh Home iPhone (`UIRectEdgeAll`)**: Tắt hẳn hiện tượng chạm gần cạnh dưới làm sáng thanh Home; cần vuốt chủ động từ đáy màn hình lên mới kích hoạt.
- **Không còn hiện lại tay cầm SDViOS cũ**: Giữ cố định chế độ điều khiển Mobile khi mở mọi bảng phụ.

---

<details>
<summary><b>🛠️ Build từ mã nguồn (dành cho lập trình viên)</b></summary>

Yêu cầu: base IPA từ [SDViosport](https://github.com/DeadAKJ/SDViosport), `zig cc` + `ld64.lld` + `iPhoneOS.sdk`, `.NET 6.0+ SDK`, `zsign`, `Mono.Cecil.dll`, Python 3.10+.

```powershell
# 1. Build mod điều khiển C# SDVMobileControls
dotnet build ModGarden\SDVMobileControls\SDVMobileControls.csproj -c Release

# 2. Vá IL (MonoGame.Framework.dll, SDViOS.dll, SDViOSTouchControls.dll)
powershell -ExecutionPolicy Bypass -File scripts\patch_perf_v7.ps1

# 3. Biên dịch SDVModGarden.framework (iOS 14–18 & iOS 26–27) và đóng gói 2 bản IPA sạch (0% tệp bản quyền)
python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios26 zsign.exe dist\Garden-Launcher-v7.10-iOS26-27.ipa `
  "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --min-ios=16.0

python scripts\package_embedded.py base-sdvios.ipa build\SDVModGarden_ios14 zsign.exe dist\Garden-Launcher-v7.10-iOS14-18.ipa `
  "$WorkDir\v7-host.dll" "$WorkDir\v7-touch.dll" "$WorkDir\Lidgren.Network.dll" --launcher --no-scene --min-ios=14.0
```

</details>

---

**Credits:** [ConcernedApe](https://www.stardewvalley.net/) · [SDViosport](https://github.com/DeadAKJ/SDViosport) (MIT) · [SMAPI](https://github.com/Pathoschild/SMAPI) (LGPL-3.0) · [miniz](https://github.com/richgel999/miniz) (MIT) — Phát hành theo [MIT License](LICENSE).
