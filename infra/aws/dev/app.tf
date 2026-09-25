resource "aws_instance" "api" {
  ami                         = data.aws_ssm_parameter.amazon_linux_2023.value
  instance_type               = var.instance_type
  subnet_id                   = aws_subnet.public.id
  vpc_security_group_ids      = [aws_security_group.api.id]
  iam_instance_profile        = aws_iam_instance_profile.api.name
  # The provider reads this as true once an Elastic IP is attached to the primary ENI.
  associate_public_ip_address = true
  monitoring                  = false
  user_data_replace_on_change = false

  credit_specification {
    cpu_credits = "standard"
  }

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 1
  }

  root_block_device {
    volume_size           = 32
    volume_type           = "gp3"
    encrypted             = true
    delete_on_termination = false
  }

  user_data = templatefile("${path.module}/user_data.sh.tftpl", {
    aws_region          = var.aws_region
    gcp_project_id      = var.gcp_project_id
    gcp_project_number  = var.gcp_project_number
    gcp_model_location  = var.model_location
    cognito_user_pool   = aws_cognito_user_pool.owner.id
    cognito_client_id   = aws_cognito_user_pool_client.desktop.id
    artifact_bucket     = aws_s3_bucket.artifacts.bucket
    api_artifact_key    = aws_s3_object.api_release.key
    gcp_credentials_key = aws_s3_object.google_credentials.key
    api_domain          = "api.${aws_eip.api.public_ip}.${var.api_domain_suffix}"
  })

  depends_on = [
    aws_iam_role_policy_attachment.ssm,
    aws_iam_role_policy.api_artifacts,
    aws_s3_object.api_release,
    aws_s3_object.google_credentials,
  ]

  tags = { Name = "autobots-dev-api" }
}

resource "aws_cloudwatch_metric_alarm" "instance_status" {
  alarm_name          = "autobots-dev-api-instance-status"
  alarm_description   = "EC2 instance status check failed for the Autobots dev API."
  namespace           = "AWS/EC2"
  metric_name         = "StatusCheckFailed_Instance"
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 2
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  dimensions          = { InstanceId = aws_instance.api.id }
}

resource "aws_cloudwatch_metric_alarm" "high_cpu" {
  alarm_name          = "autobots-dev-api-high-cpu"
  alarm_description   = "The dev API has sustained high CPU usage."
  namespace           = "AWS/EC2"
  metric_name         = "CPUUtilization"
  statistic           = "Average"
  period              = 300
  evaluation_periods  = 3
  threshold           = 85
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
  dimensions          = { InstanceId = aws_instance.api.id }
}
