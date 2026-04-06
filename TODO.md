# Unity Read-Only MCP 修改清单

基于代码审阅结论，按优先级排列。

---

## P0 - 质量与可维护性

- [ ] **拆分 `UnityReadOnlyMcpQueries.cs`**
  - 已抽出 `MaterialExportSpecBuilder.cs`
  - 已抽出 `ShaderGraphBundleBuilder.cs`
  - 查询入口已开始改为委托 builder
  - 仍需继续清理 `UnityReadOnlyMcpQueries.cs` 中残留 helper，进一步收敛到“简单查询 + 分发”

- [ ] **补自动化测试**
  - 已新增 Node 侧自动化测试，覆盖工具注册、调用分发、结果归一化
  - 仍需补 Unity/C# 侧 NUnit 测试，覆盖材质导出语义映射、ShaderGraph 解析和 DTO 往返

- [x] **统一 C# 层异常处理**
  - 统一错误响应：`errorCode + error + context + details`
  - 所有 HTTP 失败路径返回结构化 JSON

- [x] **拆分 `src/index.js`**
  - 已按领域拆为 `src/tools/assets.js`、`materials.js`、`shaders.js`、`scenes.js`、`prefabs.js`、`textures.js`
  - `src/index.js` 只保留 server 初始化和工具注册

---

## P1 - 功能补全（TA/渲染方向）

- [x] **Render Pipeline 配置查询**
  - 路由：`/api/pipeline/info`
  - 返回当前激活 pipeline、默认 pipeline、质量等级到 pipeline 的绑定，以及当前质量级关键参数

- [x] **增强场景查询**
  - 新增 `get_scene_lights` / `get_scene_volumes`
  - 支持按 `scenePath`、`layers`、`tag` 过滤
  - Light / ReflectionProbe / Volume 输出已补更多细节字段

- [x] **Prefab 查询**
  - 路由：`/api/prefabs/info`
  - 支持 Prefab 层级、组件结构、材质引用查询

- [x] **纹理资产查询**
  - 路由：`/api/textures/info`
  - 支持导入设置、平台 override、基础纹理元数据查询

---

## P2 - 功能补全（通用方向，按需）

- [x] **Animation/Animator 查询**
  - 路由：`/api/animations/info`
  - 支持 AnimatorController 状态机层/参数、AnimationClip 曲线绑定和事件查询

- [x] **项目设置查询**
  - 路由：`/api/project/settings`
  - 支持 Player Settings、当前构建目标和关键 Graphics Settings 读取

- [x] **Package 信息查询**
  - 路由：`/api/project/packages`
  - 读取项目 `Packages/manifest.json` 依赖列表

---

## P3 - 改进与优化

- [ ] **导出 profile 可扩展化**
  - 当前仍以内置 `"ue-pbr"` 为主
  - 后续应抽为独立配置或策略类

- [x] **健康检查增强**
  - `/health` 已返回 timeout 和兼容性摘要

- [x] **请求日志与诊断**
  - 通过 `UNITY_MCP_LOG_REQUESTS` 开启请求/响应日志和耗时输出

- [x] **超时配置统一**
  - JS 侧读取 `UNITY_MCP_TIMEOUT_MS`
  - Unity 侧读取同一环境变量，并增加少量缓冲
