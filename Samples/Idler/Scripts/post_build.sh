#!/bin/bash

echo "Build Result: $BUILD_RESULT"
echo "Build Platform: $BUILD_PLATFORM"
echo "Build Revision: $BUILD_REVISION"
echo "Unity Player Path: $UNITY_PLAYER_PATH"
echo "Output Dir: $OUTPUT_DIRECTORY"
echo "Build Target: $BUILD_TARGET"
echo "Organisation ID: $ORG_ID"
echo "Project ID: $PROJECT_ID"
echo "Build Number: $UCB_BUILD_NUMBER"
echo "Branch: $SCM_BRANCH"

# Define necessary variables
REPO_OWNER="metaplay"
REPO_NAME="sdk"
WORKFLOW_ID="unity-cloud-build-processing.yaml"

# Prepare payload with workflow_dispatch inputs
PAYLOAD=$(cat <<EOF
{
  "ref": "$SCM_BRANCH",
  "inputs": {
    "build_result": "$BUILD_RESULT",
    "build_platform": "$BUILD_PLATFORM",
    "build_revision": "$BUILD_REVISION",
    "build_target": "$BUILD_TARGET",
    "org_id": "$ORG_ID",
    "project_id": "$PROJECT_ID",
    "build_number": "$UCB_BUILD_NUMBER",
    "branch": "$SCM_BRANCH"
  }
}
EOF
)

# Trigger workflow_dispatch event on GitHub
RESPONSE=$(curl -X POST \
     -H "Accept: application/vnd.github+json" \
     -H "Authorization: Bearer $GITHUB_TOKEN" \
     -H "X-GitHub-Api-Version: 2022-11-28" \
     "https://api.github.com/repos/$REPO_OWNER/$REPO_NAME/actions/workflows/$WORKFLOW_ID/dispatches" \
     -d "$PAYLOAD")

# Print response
echo "Response: $RESPONSE"

exit 0