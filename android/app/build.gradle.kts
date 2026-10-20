plugins {
    id("com.android.application")
}

android {
    namespace = "com.droptowin"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.droptowin"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "1.0.0"
    }

    buildTypes {
        release {
            isMinifyEnabled = false
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_1_8
        targetCompatibility = JavaVersion.VERSION_1_8
    }
}
