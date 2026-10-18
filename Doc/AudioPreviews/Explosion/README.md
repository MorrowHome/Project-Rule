# 当前爆炸音效试听

素材：Kenney [Sci-Fi Sounds](https://kenney.nl/assets/sci-fi-sounds) / explosionCrunch_000，CC0。

Explosion_Audition.wav 为当前爆炸素材的单次试听，乘以事件音量0.65及SfxPlayer主音量0.85，不含距离衰减；游戏内播放原OGG。试听不包含引信。

生成命令：ffmpeg -i Project-Rule/Assets/_Project/Audio/SFX/Kenney/SciFi/explosionCrunch_000.ogg -af volume=0.5525 -ac 1 -ar 44100 -c:a pcm_s16le Doc/AudioPreviews/Explosion/Explosion_Audition.wav
