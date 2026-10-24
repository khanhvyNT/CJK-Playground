using System;

namespace CJKPlayground.Services
{
    /// <summary>
    /// Toàn bộ các thông số vật lý và hằng số của Bộ máy Tối ưu Bố cục Quang học (6-Stage Solver)
    /// Tập trung duy nhất tại đây, không rải rác trong mã nguồn giải thuật.
    /// </summary>
    public class SolverConfig
    {
        // 1. Khoảng cách & Lề an toàn (Đơn vị em-box 1000x1000)
        public float GTarget = 26.0f;              // Khoảng hở mực tối thiểu mục tiêu giữa các nhánh
        public float MinHoleWidth = 16.0f;          // Bề rộng khe trắng nội tại tối thiểu bảo vệ lỗ
        public float SafeMinX = 112.0f;             // Khung an toàn mặc định Mễ Tự Cách MinX
        public float SafeMaxX = 888.0f;             // Khung an toàn mặc định Mễ Tự Cách MaxX
        public float SafeMinY = 112.0f;             // Khung an toàn mặc định Mễ Tự Cách MinY
        public float SafeMaxY = 888.0f;             // Khung an toàn mặc định Mễ Tự Cách MaxY

        // 2. Tham số Nguyên mẫu & Láng giềng k-NN
        public float AlphaMax = 0.60f;              // Trọng số ảnh hưởng tối đa của nguyên mẫu
        public float Sigma = 0.35f;                 // Độ rộng hàm Gaussian khoảng cách đặc trưng
        public float CMin = 0.15f;                  // Ngưỡng tin cậy tối thiểu (dưới mức này chuyển sang Tổng quát)
        public int TopK = 3;                        // Số láng giềng gần nhất k-NN trong không gian đặc trưng

        // 3. Quy luật Lũy thừa phân bổ tỷ lệ chia khung
        public float Gamma = 0.44f;                 // Số mũ Sub-linear Power Law (S1^gamma / (S1^gamma + S2^gamma))
        public float SplitMin = 0.26f;              // Giới hạn dưới tỷ lệ chia khung
        public float SplitMax = 0.74f;              // Giới hạn trên tỷ lệ chia khung
        public float TernaryMin = 0.18f;            // Giới hạn dưới tỷ lệ mỗi cột/tầng của ⿲/⿳
        public float LambdaSurround = 0.50f;        // Hệ số tổng hợp mật độ toán tử bao quanh

        // 4. Bệ đỡ & Sải ngang (Width Harmony)
        public float STopDefault = 0.86f;           // Tỷ lệ thu hẹp sải ngang nhánh Trên mặc định
        public float SBotDefault = 0.98f;           // Tỷ lệ mở rộng sải ngang nhánh Dưới mặc định

        // 5. Bù nét dị hướng (Anisotropic Stroke Dilation)
        public float DilateCoeff = 0.55f;           // Hệ số bù độ đậm nét ((T_font - W') * 0.55)
        public float DilateStep = 0.5f;             // Bước lùi an toàn khi giải phóng lỗ trắng bị bít

        // 6. Dò hốc trống động (Cavity Allocation)
        public float CavityBuffer = 26.0f;          // Đệm mặt nạ mực khi tìm hốc trống
        public float CavityMinRatio = 0.18f;        // Tỷ lệ kích thước tối thiểu mỗi chiều của hốc (18% em-box)

        // 7. Trọng số Hàm mục tiêu Tối ưu J(T)
        public float W1Gap = 1.5f;                  // Phạt sai lệch khoảng cách mực GTarget
        public float W2Weight = 1.0f;               // Phạt sai lệch độ đậm nét so với T_font
        public float W3Density = 1.2f;              // Phạt chênh lệch độ xám giữa các nhánh
        public float W4Centroid = 1.0f;             // Phạt lệch tâm Mễ Tự Cách
        public float W5CounterSpace = 100.0f;       // Ràng buộc cứng bảo tồn lỗ trắng
        public float W6Prior = 1.2f;                // Phạt sai lệch nguyên mẫu (nhân trọng số alpha)

        // 8. Tinh chỉnh Tọa độ Giới hạn (Bounded Coordinate Descent)
        public int MaxIterations = 12;              // Số vòng lặp tối đa
        public float SplitStep = 0.02f;             // Bước dịch tỷ lệ chia khung
        public float OffsetStep = 4.0f;             // Bước dịch tọa độ px
    }
}
