# Workflow [ V3 ]

## 🏗️ 层次结构

> [ Components.xmind ] 文件描述了核心组件的 ViewModel 层次结构

> [ Helpers.xmind ] 文件描述了一组可继承、可重写的核心逻辑类，是对工作流实施定制、扩展的关键

## 💡 相比于 [ V2 ]

- ### 性能提升
  - 连接查询
  - 坐标更新
  - 任务传播
- ### 功能增加
  - Broadcast 支持并发验证与排队传播
  - 支持撤销和重做
- ### 易用性与扩展性
  - ViewModel 模板生成改进
  - Helper 全过程可继承、可重写