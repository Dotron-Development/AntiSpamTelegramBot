variable "location" {
  description = "The location of the Open AI services account."
  type        = string
}

variable "environment_prefix" {
  description = "The prefix to use for all resources."
  type        = string
}

variable "environment_name" {
  description = "The environment name."
  type        = string
}

variable "image_text_extractor_capacity" {
  description = "The capacity of the image text extractor deployment."
  type        = number
}

variable "spam_recognition_capacity" {
  description = "The capacity of the spam recognition deployment."
  type        = number
}
