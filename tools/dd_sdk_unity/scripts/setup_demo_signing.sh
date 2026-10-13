#!/bin/zsh

set +x
set -eo pipefail
source ./code-sign.sh

P12_PATH="dev_cert.p12"
PP_PATH="demo.mobileprovision"

vault kv get -field=value kv/aws/arn:aws:iam::486234852809:role/ci-dd-sdk-unity/dev_cert_base64 | base64 --decode -o $P12_PATH
vault kv get -field=value kv/aws/arn:aws:iam::486234852809:role/ci-dd-sdk-unity/demo_provisioning_profile_base64 | base64 --decode -o $PP_PATH

export KEY_SECRET=`vault kv get -field=value kv/aws/arn:aws:iam::486234852809:role/ci-dd-sdk-unity/dev_cert_password`

install_provisioning_profile $PP_PATH

create_keychain
keychain_import \
    --p12 $P12_PATH \
    --p12-password $KEY_SECRET
