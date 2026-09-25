resource "aws_iam_role" "api" {
  name = "autobots-dev-api"
  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect    = "Allow"
      Principal = { Service = "ec2.amazonaws.com" }
      Action    = "sts:AssumeRole"
    }]
  })
  tags = { Name = "autobots-dev-api" }
}

resource "aws_iam_instance_profile" "api" {
  name = "autobots-dev-api"
  role = aws_iam_role.api.name
}

resource "aws_iam_role_policy_attachment" "ssm" {
  role       = aws_iam_role.api.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy" "api_artifacts" {
  name = "autobots-dev-artifact-access"
  role = aws_iam_role.api.id
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = ["s3:GetObject"]
        Resource = [
          "${aws_s3_bucket.artifacts.arn}/${aws_s3_object.api_release.key}",
          "${aws_s3_bucket.artifacts.arn}/${aws_s3_object.google_credentials.key}"
        ]
      },
      {
        Effect   = "Allow"
        Action   = ["s3:PutObject"]
        Resource = "${aws_s3_bucket.artifacts.arn}/backups/*"
      },
      {
        Effect    = "Allow"
        Action    = ["s3:ListBucket"]
        Resource  = aws_s3_bucket.artifacts.arn
        Condition = { StringLike = { "s3:prefix" = ["releases/*", "config/*", "backups/*"] } }
      }
    ]
  })
}
