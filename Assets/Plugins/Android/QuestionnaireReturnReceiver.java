package org.thebigredbuttoninstitute.questionnaire;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;
import java.util.List;

public final class QuestionnaireReturnReceiver extends BroadcastReceiver {
    @Override
    public void onReceive(Context context, Intent intent) {
        if (context == null) {
            return;
        }

        QuestionnaireResultStore.markCallback(context, requestIdFrom(intent));
        Log.i("BRBQuestionnaire", QuestionnaireResultStore.readLatestResultSummary(context));
    }

    private static String requestIdFrom(Intent intent) {
        if (intent == null) {
            return "";
        }

        String requestId = intent.getStringExtra(QuestionnaireContract.ExtraRequestId);
        if (requestId != null && !requestId.trim().isEmpty()) {
            return requestId;
        }

        Uri data = intent.getData();
        if (data == null) {
            return "";
        }

        List<String> segments = data.getPathSegments();
        if (segments == null || segments.isEmpty()) {
            return "";
        }

        return segments.get(segments.size() - 1);
    }
}
