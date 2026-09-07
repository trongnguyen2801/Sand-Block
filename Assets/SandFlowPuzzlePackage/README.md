# Sand Flow Puzzle Package

Thư mục này chứa toàn bộ asset runtime, editor, UI và dependency đã dùng bởi game 16.

## Yêu cầu project đích

- Unity 6000.4 hoặc mới hơn.
- Universal Render Pipeline (URP).
- Input System (`com.unity.inputsystem`).
- TextMesh Pro Essentials. Trong Unity chọn `Window > TextMeshPro > Import TMP Essential Resources` nếu project chưa có.
- `Active Input Handling` đặt thành `Input System Package (New)` hoặc `Both`.

## Đóng gói

1. Mở scene đang chứa GameObject `#SandFlowPuzzle` nếu muốn kèm prefab hoàn chỉnh.
2. Chọn `Tools > Sand Flow Puzzle > Export Unity Package`.
3. File `SandFlowPuzzlePackage.unitypackage` được tạo ở thư mục gốc project.

Exporter chỉ đóng gói `Assets/SandFlowPuzzlePackage`. Tất cả dependency riêng của game đã được chuyển vào đây; TMP Essentials vẫn dùng bản chuẩn của project đích.

## Sau khi import

1. Import TMP Essential Resources nếu Unity báo thiếu font.
2. Mở `Scenes/SandFlowPuzzleTest.unity` và nhấn Play để test ngay.
3. Hoặc thêm prefab `Prefabs/SandFlowPuzzleGame.prefab` vào scene riêng (nếu prefab đã được capture khi export).
4. Đảm bảo scene riêng có `Camera` gắn tag `MainCamera`; `EventSystem` và World Canvas sẽ được tự tạo nếu còn thiếu.

Level JSON nằm tại `Resources/Levels`. Shader hạt tròn và các âm thanh cũng nằm trong `Resources` để runtime tự nạp.
