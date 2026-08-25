# Regras ProGuard para o VoxIA Keyboard
-keep class com.voxia.keyboard.data.** { *; }
-keepclassmembers class * {
    @com.google.gson.annotations.SerializedName <fields>;
}
