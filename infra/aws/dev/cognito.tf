resource "aws_cognito_user_pool" "owner" {
  name                     = "autobots-dev-owner"
  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]
  mfa_configuration        = "OFF"

  admin_create_user_config {
    allow_admin_create_user_only = true
    invite_message_template {
      email_subject = "Autobots by Origin Studios owner sign-in"
      email_message = "Your owner account for Autobots is ready. Username: {username}. Temporary password: {####}. Sign in using the Autobots desktop app and choose a new password."
      sms_message   = "Autobots {username} sign-in code: {####}"
    }
  }

  password_policy {
    minimum_length                   = 12
    require_lowercase                = true
    require_numbers                  = true
    require_symbols                  = true
    require_uppercase                = true
    temporary_password_validity_days = 7
    password_history_size            = 5
  }

  account_recovery_setting {
    recovery_mechanism {
      name     = "verified_email"
      priority = 1
    }
  }

  verification_message_template {
    default_email_option = "CONFIRM_WITH_CODE"
    email_subject        = "Autobots email verification"
    email_message        = "Your Autobots verification code is {####}."
  }

  tags = { Name = "autobots-dev-owner" }
}

resource "aws_cognito_user_group" "owners" {
  name         = "autobots-owners"
  user_pool_id = aws_cognito_user_pool.owner.id
  description  = "Owner-only Autobots API access."
  precedence   = 1
}

resource "aws_cognito_user_pool_client" "desktop" {
  name         = "autobots-dev-desktop"
  user_pool_id = aws_cognito_user_pool.owner.id

  generate_secret                      = false
  enable_token_revocation              = true
  prevent_user_existence_errors        = "ENABLED"
  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code"]
  allowed_oauth_scopes                 = ["openid", "email", "profile"]
  callback_urls                        = ["http://127.0.0.1:53682/auth/callback"]
  logout_urls                          = ["http://127.0.0.1:53682/auth/logout"]
  default_redirect_uri                 = "http://127.0.0.1:53682/auth/callback"
  supported_identity_providers         = ["COGNITO"]
  access_token_validity                = 15
  id_token_validity                    = 15
  refresh_token_validity               = 7
  token_validity_units {
    access_token  = "minutes"
    id_token      = "minutes"
    refresh_token = "days"
  }
  explicit_auth_flows = ["ALLOW_USER_SRP_AUTH", "ALLOW_REFRESH_TOKEN_AUTH"]
}

resource "aws_cognito_user_pool_domain" "hosted_ui" {
  domain       = "autobots-dev-${var.expected_account_id}"
  user_pool_id = aws_cognito_user_pool.owner.id
}
