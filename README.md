# RobTeach

Robot teaching

## 1. 引言

本报告为上位机换型软件的整体设计、技术路线和实现方式提供详细方案。该软件旨在支持不同产品换型时，通过解析CAD图纸生成机械臂喷淋轨迹，允许用户选择轨迹、设置喷嘴和喷淋类型，保存配置，并通过Modbus协议与机械臂通信。以下内容涵盖需求分析、系统架构、技术选型、模块设计、实现步骤和注意事项。

## 2. 需求分析

根据用户需求，软件需实现以下功能：

- **CAD图纸解析**：支持DXF格式，提取几何实体（线段、圆弧、圆）。
- **轨迹生成**：将选定CAD实体转换为机械臂运动轨迹，轨迹可以由点、半径和角度信息组成。
- **用户交互**：提供直观界面，允许用户选择轨迹、设置喷嘴编号和喷淋类型（喷气或喷水）。
- **配置管理**：保存配置到本地JSON文件，支持加载和修改。
- **Modbus通信**：将配置数据发送到机械臂控制器，驱动其执行喷淋任务。

### 2.1 假设与约束

- CAD图纸主要为2D DXF格式，机械臂喷淋任务在固定Z高度的2D平面进行。
- 机械臂控制器支持Modbus协议，但具体寄存器映射需进一步确认。
- 软件运行于Windows平台，需支持高分辨率显示和用户友好交互。

## 3. 系统架构

软件采用模块化架构，分为以下核心模块：

- **CAD解析模块**：负责加载和解析DXF文件，提取几何实体。
- **轨迹生成模块**：根据基元（线段、圆、圆弧），用起点、终点、半径、角度等参数生成轨迹。
- **用户界面模块**：提供WPF界面，显示CAD图纸、支持交互操作。
- **配置管理模块**：处理轨迹和参数的保存与加载。
- **Modbus通信模块**：通过Modbus协议与机械臂控制器通信。

### 架构图

| 模块       | 功能描述                            | 依赖库/技术       |
| ---------- | ----------------------------------- | ----------------- |
| CAD解析    | 解析DXF文件，提取线段、圆弧等实体   | netDxf            |
| 轨迹生成   | 将实体转换为机械臂轨迹信息          | 自定义算法        |
| 用户界面   | 显示CAD图纸，支持轨迹选择和参数设置 | WPF               |
| 配置管理   | 保存和加载JSON配置                  | JSON.NET          |
| Modbus通信 | 发送轨迹数据到机械臂控制器          | EasyModbusTCP.NET |

## 4. 技术选型

### 4.1 编程语言与框架

- **C#**：成熟的Windows开发语言，支持丰富的库和工具，适合快速开发。
- **WPF**：提供矢量图形渲染，适合显示CAD图纸，支持复杂的用户交互。

### 4.2 CAD解析

- **netDxf**（[netDxf GitHub](https://github.com/haplokuon/netDxf)）：开源库，支持AutoCAD 2000至2018的DXF格式，解析线段、圆弧、折线等2D实体。
- 理由：DXF是工业标准格式，netDxf轻量且易于集成，适合2D图纸解析。

### 4.3 轨迹生成

- 自定义算法处理几何实体：
  - 线段：直接提取起点和终点。
  - 圆弧：根据用户定义的分辨率离散化为点序列。
  - 折线：按顺序连接各段的点。
- 坐标变换：支持用户设置缩放、偏移或旋转，以匹配机械臂工作空间。

### 4.4 用户界面

- 使用WPF的Canvas和Shape类绘制CAD实体。
- 支持交互功能：缩放、平移、实体选择、参数设置。
- 提供轨迹预览，确保用户确认轨迹正确性。

### 4.5 配置管理

- 使用JSON.NET序列化轨迹数据和参数。

- 数据结构示例：

  ```csharp
  public class Trajectory {
      public List<Point> Points { get; set; } // 轨迹点列表
      public int NozzleNumber { get; set; }   // 喷嘴编号
      public bool IsWater { get; set; }       // 喷水（true）或喷气（false）
  }
  public class Configuration {
      public List<Trajectory> Trajectories { get; set; } // 最多五条轨迹
      public string ProductName { get; set; }            // 产品名称
      public Transform Transform { get; set; }           // 坐标变换参数
  }
  ```

### 4.6 Modbus通信

- **EasyModbusTCP.NET**（[EasyModbusTCP.NET GitHub](https://github.com/rossmann-engineering/EasyModbusTCP.NET)）：支持Modbus TCP、UDP和RTU，API简单，适合工业自动化。

- 数据传输假设：

  - 发送轨迹数量、点坐标、喷嘴编号和喷淋类型到指定寄存器。

  - 示例寄存器映射（需确认）：

    | 寄存器地址 | 数据内容                    |
    | ---------- | --------------------------- |
    | 1000       | 轨迹数量                    |
    | 1001       | 轨迹1点数                   |
    | 1002-1051  | 轨迹1 X坐标                 |
    | 1052-1101  | 轨迹1 Y坐标                 |
    | 1102       | 轨迹1喷嘴编号               |
    | 1103       | 轨迹1喷淋类型（0=气，1=水） |

## 5. 实现步骤

### 5.1 项目初始化

- 创建C# WPF项目，安装NuGet包：netDxf、EasyModbusTCP、Newtonsoft.Json。
- 配置项目支持.NET Framework或.NET Core，确保兼容性。

### 5.2 CAD解析模块

- 使用netDxf加载DXF文件：

  ```csharp
  DxfDocument dxf = DxfDocument.Load("sample.dxf");
  var lines = dxf.Lines;
  var arcs = dxf.Arcs;
  var polylines = dxf.LwPolylines;
  ```

- 将实体转换为WPF可绘制的形状，显示在Canvas上。

### 5.3 轨迹生成模块

- 实现算法将实体转换为点序列：

  - 线段：直接使用起点和终点。
  - 圆弧：根据角度和半径，生成等间隔点。
  - 折线：遍历顶点，连接各段。

- 示例代码：

  ```csharp
  public List<Point> ConvertArcToPoints(Arc arc, double resolution) {
      List<Point> points = new List<Point>();
      double startAngle = arc.StartAngle;
      double endAngle = arc.EndAngle;
      double radius = arc.Radius;
      for (double angle = startAngle; angle <= endAngle; angle += resolution) {
          double x = arc.Center.X + radius * Math.Cos(angle * Math.PI / 180);
          double y = arc.Center.Y + radius * Math.Sin(angle * Math.PI / 180);
          points.Add(new Point(x, y));
      }
      return points;
  }
  ```

### 5.4 用户界面模块

- 设计主窗口，包含：
  - CAD显示区域：使用Canvas绘制实体。
  - 轨迹选择面板：列表或选项卡显示最多五条轨迹。
  - 参数设置区域：喷嘴编号和喷淋类型输入框。
  - 按钮：加载CAD、保存/加载配置、发送数据。
- 支持交互：
  - 鼠标点击选择实体，高亮显示。
  - 缩放和平移功能，增强用户体验。
  - 轨迹预览，显示生成的点序列。

### 5.5 配置管理模块

- 实现保存和加载功能：

  ```csharp
  public void SaveConfig(Configuration config, string filePath) {
      string json = JsonConvert.SerializeObject(config, Formatting.Indented);
      File.WriteAllText(filePath, json);
  }
  public Configuration LoadConfig(string filePath) {
      string json = File.ReadAllText(filePath);
      return JsonConvert.DeserializeObject<Configuration>(json);
  }
  ```

### 5.6 Modbus通信模块

- 配置Modbus客户端：

  ```csharp
  ModbusClient modbusClient = new ModbusClient("192.168.0.1", 502);
  modbusClient.Connect();
  ```

- 发送轨迹数据：

  ```csharp
  modbusClient.WriteSingleRegister(1000, config.Trajectories.Count);
  for (int i = 0; i < config.Trajectories.Count; i++) {
      var traj = config.Trajectories[i];
      modbusClient.WriteSingleRegister(1001 + i * 100, traj.Points.Count);
      // 写入X、Y坐标、喷嘴编号和喷淋类型
  }
  modbusClient.Disconnect();
  ```

### 5.7 测试与优化

- **单元测试**：测试CAD解析、轨迹生成和配置管理模块。
- **集成测试**：使用Modbus模拟器（如Modbus Slave）验证通信。
- **性能优化**：优化CAD渲染和轨迹生成算法，处理大型DXF文件。
- **用户反馈**：根据实际使用调整界面和功能。

## 6. 注意事项

- **Modbus寄存器映射**：需获取机械臂控制器的Modbus文档，确认数据格式和寄存器分配。
- **坐标系匹配**：确保CAD坐标与机械臂工作空间一致，可能需用户设置变换参数。
- **错误处理**：
  - 无效CAD文件：提示用户重新选择。
  - 通信失败：重试机制或错误提示。
  - 轨迹无效：验证点序列连续性。
- **扩展性**：支持3D CAD（如STEP格式）或更多轨迹，需使用Open CASCADE Technology（[Open CASCADE](https://www.opencascade.com/)）。
- **安全性**：确保Modbus通信加密（如使用VPN），防止数据泄露。

## 7. 开发计划

| 阶段          | 任务内容                             | 预计时间 |
| ------------- | ------------------------------------ | -------- |
| 项目初始化    | 搭建C# WPF项目，集成库               | 1周      |
| CAD解析与显示 | 实现DXF解析和WPF渲染                 | 2周      |
| 轨迹生成      | 开发实体到点序列的转换算法           | 2周      |
| 用户界面      | 设计交互界面，支持轨迹选择和参数设置 | 3周      |
| 配置管理      | 实现JSON保存和加载                   | 1周      |
| Modbus通信    | 实现数据发送，测试通信               | 2周      |
| 测试与优化    | 单元测试、集成测试、性能优化         | 3周      |

## 8. 结论

本设计方案通过模块化架构和成熟技术栈，满足用户对上位机换型软件的需求。使用C#和WPF开发，结合netDxf和EasyModbusTCP.NET，确保功能实现和用户体验。后续开发需重点确认机械臂的Modbus接口，并根据用户反馈优化功能。
