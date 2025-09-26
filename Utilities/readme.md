# Utilities for SurveyBackend

本项目包含用于制作问卷包、调试 AI 见解的工具集 `Utilities` 。

## 制作问卷包

你可以编译本项目，然后使用本项目提供的交互式打包工具。

编译项目后，执行指令: 

```
./Utilities packSurvey
```

根据提示操作即可。

## 调试 AI 见解

目前尚未提供 AI 见解的交互式调试工具。但你可以通过修改 `LLMTools.cs` 中的配置文件简要调试你的 AI 配置和系统提示词。修改后编译项目，然后执行指令:

```
./Utilities llmtest
```

根据提示传入问卷原始json和提交结果即可。