# 玩家反馈试听候选

当前采用 Hurt_A/ Land_A 用于玩家受击与落地，B 保留对照；准备脚本本身不修改游戏配置。来源为 Kenney Impact Sounds，CC0；原件在 Doc/AudioSources/FeedbackOriginals，处理与哈希在 PlayerFeedbackManifest.json。

| 候选 | 方向 | 原素材 |
| --- | --- | --- |
| Hurt_A | 短钝击，裁短尾音 | impactPunch_heavy_003 |
| Hurt_B | 软材质撞击 | impactSoft_medium_003 |
| Land_A | 软脚步 | footstep_carpet_000 |
| Land_B | 轻鞋底接触 | footstep_concrete_002 |

不带叫声、呻吟或木板敲击。低通与末尾淡出减轻高频和拖尾；实际听感由用户选择。

无后缀 WAV 为可接入素材；_Audition.wav 每段重复三次，已乘拟定事件音量（受击0.60/落地0.33）与主音量0.85。接入时使用无后缀版本，避免重复乘音量。两种受击、两种落地分别匹配近似RMS并保留峰值余量。

复现：python3 Tools/Audio/prepare_player_feedback.py /path/to/kenney_impact-sounds.zip，需要 ffmpeg。
