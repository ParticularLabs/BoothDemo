import random
import string
import pika
import uuid
from datetime import datetime, UTC

RABBIT_HOST = "localhost"

def random_message_id():
    return "".join(random.choice(string.ascii_uppercase) for _ in range(4))

connection = pika.BlockingConnection(
    pika.ConnectionParameters(host=RABBIT_HOST)
)

channel = connection.channel()

channel.queue_declare(queue="Button", durable=True, arguments={"x-queue-type": "quorum"})
channel.queue_declare(queue="Sales", durable=True, arguments={"x-queue-type": "quorum"})

def callback(ch, method, properties, body):
    msg_id = random_message_id()

    channel.basic_publish(
        exchange="",
        routing_key="Sales",
        body=body,
        properties=pika.BasicProperties(
            message_id=msg_id,
            content_type="application/json",
            delivery_mode=2,
            headers={
                "NServiceBus.EnclosedMessageTypes": "Messages.PlaceOrder, Messages, Version=1.0.0.0, Culture=neutral, PublicKeyToken=a13f7b099d0ea862",
                "NServiceBus.MessageId": msg_id,
                "NServiceBus.CorrelationId" : msg_id,
                "NServiceBus.ConversationId": str(uuid.uuid4()),
                "NServiceBus.MessageIntent" : "Send",
                "NServiceBus.OriginatingEndpoint" : "ClientUI",
                "NServiceBus.OriginatingMachine" : "TheButton",
                "NServiceBus.ReplyToAddress" : "ClientUI",
		"MonitoringDemo.ManualMode" : "True",
                "NServiceBus.TimeSent" : datetime.now(UTC).strftime("%Y-%m-%d %H:%M:%S:%f Z"),
            }
        )
    )

    ch.basic_ack(delivery_tag=method.delivery_tag)

channel.basic_consume(
    queue="Button",
    on_message_callback=callback
)

print("MQTT bridge running...")
channel.start_consuming()
