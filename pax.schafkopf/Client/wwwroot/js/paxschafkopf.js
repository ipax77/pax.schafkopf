var noSleep = new NoSleep();
var wakeLockEnabled = false;

window.GetSize = (element) => {
    var elmnt = document.getElementById(element);
    return [elmnt.offsetWidth, window.innerHeight];
};

window.CopyToClipboard = (element) => {
    var elmnt = document.getElementById(element);
    var r = document.createRange();
    r.selectNode(elmnt);
    window.getSelection().removeAllRanges();
    window.getSelection().addRange(r);
    document.execCommand('copy');
    window.getSelection().removeAllRanges();
};

window.addEventListener('load', function () {
    function updateOnlineStatus(event) {
        IsOnline(navigator.onLine);
    }
    window.addEventListener('online', updateOnlineStatus);
    window.addEventListener('offline', updateOnlineStatus);
});

window.CheckOnlineStatus = () => {
    return navigator.onLine;
};

window.IsOnline = (onlinestatus) => {
    try {
        DotNet.invokeMethodAsync('sc2dsstats.pwa.Client', 'SetOnlineStatus', onlinestatus);
    } catch {
        console.log("failed setting online status.");
    }
};

window.IsOnline = (onlinestatus) => {
    try {
        DotNet.invokeMethodAsync('pax.schafkopf.Client', 'SetOnlineStatus', onlinestatus);
    } catch {
        console.log("failed setting online status.");
    }
};

window.TrickAnimation = (id) => {
    var element = document.getElementById(id);
    if (element != null) {
        element.classList.add("collecttrick");
        setTimeout(() => { element.classList.remove("collecttrick"); }, 2500);
    }
};

window.ScreenSleep = () => {
    
    var toggleEl = document.querySelector("#screentoggle");
    toggleEl.addEventListener('click', function () {
        if (!wakeLockEnabled) {
            noSleep.enable(); // keep the screen on!
            wakeLockEnabled = true;
            toggleEl.value = "Display an";
            toggleEl.classList.remove("btn-outline-secondary");
            toggleEl.classList.add("btn-outline-primary");
        } else {
            noSleep.disable(); // let the screen turn off.
            wakeLockEnabled = false;
            toggleEl.value = "Display aus";
            toggleEl.classList.remove("btn-outline-primary");
            toggleEl.classList.add("btn-outline-secondary");
        }
    }, false);
}

window.CardTouch = (id, card) => {
    var element = document.getElementById(id);
    
}