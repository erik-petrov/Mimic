
module.exports = {
    pwa: {
        name: "Mimic",
        themeColor: "#9b59b6",
        msTileColor: "#9b59b6",
        appleMobileWebAppCapable: "yes",
        appleMobileWebAppStatusBarStyle: "black-translucent",
        // Let a new version take over as soon as it is installed, instead of waiting until
        // every window of the app is closed, which on phones can take days.
        workboxOptions: {
            skipWaiting: true,
            clientsClaim: true,
            // Serve Workbox from this server instead of Google's CDN, so updates don't depend on it.
            importWorkboxFrom: "local"
        },
        iconPaths: {
            favicon16: "icons/icon-16x16.png",
            favicon32: "icons/icon-32x32.png",
            appleTouchIcon: "icons/icon-152x152.png",
            msTileImage: "icons/icon-144x144.png"
        }
    }
};