var UpdatePanelShrinker = {
    ModifyDefaultRenderingMechanism: function (useHandler) {
        if (window.Sys && Sys.WebForms && Sys.WebForms.PageRequestManager) {
            var prm = Sys.WebForms.PageRequestManager.getInstance();
            prm._updatePanel = function (updatePanelElement, rendering) {
                for (var updatePanelID in this._scriptDisposes) {
                    if (this._elementContains(updatePanelElement, document.getElementById(updatePanelID))) {
                        var disposeScripts = this._scriptDisposes[updatePanelID];
                        for (var i = 0; i < disposeScripts.length; i++) {
                            eval(disposeScripts[i]);
                        }

                        delete this._scriptDisposes[updatePanelID];
                    }
                }

                Sys.Application.disposeElement(updatePanelElement, true);

                var result = UpdatePanelShrinker.ApplyPatch(updatePanelElement, rendering);
                switch (result) {
                    case 'OK':
                        updatePanelElement.shrinkFailed = null;
                        break;
                    default:
                        if (updatePanelElement.shrinkFailed) {
                            alert('Error updating panel ' + updatePanelElement.id + ': Failed retry.');
                        } else {
                            if (useHandler) {
                                UpdatePanelShrinker.ForceNormalRefresh(updatePanelElement);
                            } else {
                                alert('Error updating panel ' + updatePanelElement.id + ': ' + result);
                            }
                        }
                        break;
                }
            };
        }
    },

    ApplyPatch: function (elem, patch) {
        if (!elem) return 'MissingUpdatePanel';
        var result = 'OK';
        if ((patch.length >= 4) && (patch.substring(0, 4) == 'pat|')) {
            var previousContent = elem.previousContent;
            if (!previousContent) return 'MissingPreviousContent';
            var index = patch.indexOf('|', 4);
            var contentLength = parseInt(patch.substr(4, index - 4));
            index++;
            while (index < patch.length) {
                var i = patch.indexOf('|', index);
                var start = parseInt(patch.substr(index, i - index));
                var i2 = patch.indexOf('|', i + 1);
                var length1 = parseInt(patch.substr(i + 1, i2 - i - 1));
                var i3 = patch.indexOf('|', i2 + 1);
                var length2 = parseInt(patch.substr(i2 + 1, i3 - i2 - 1));
                var replace = patch.substr(i3 + 1, length2);
                previousContent = previousContent.substr(0, start) + replace + previousContent.substr(start + length1);
                index = i3 + length2 + 2;
            }
            if (previousContent.length != contentLength) result = 'CheckFailed';
            patch = previousContent;
        }
        elem.previousContent = patch;
        elem.innerHTML = patch;
        return result;
    },

    ForceNormalRefresh: function (updatePanel, commandHandler) {
        updatePanel.shrinkFailed = true;
        var updatePanelId = updatePanel.id;

        var wRequest = new Sys.Net.WebRequest();
        wRequest.get_headers()['Cache-Control'] = 'no-cache';
        wRequest.updatePanelId = updatePanelId;
        wRequest.set_url(commandHandler);
        wRequest.set_httpVerb('GET');
        wRequest.add_completed(UpdatePanelShrinker.OnForceNormalRefreshCompleted);
        wRequest.invoke();
    },

    OnForceNormalRefreshCompleted: function (executor, eventArgs) {
        if (executor.get_responseAvailable()) {
            var updatePanelId = executor._webRequest.updatePanelId;
            if (updatePanelId) {
                updatePanelElement = document.getElementById(updatePanelId);
                if (updatePanelElement) {
                    __doPostBack(updatePanelId, '');
                }
            }
        }
    }

}