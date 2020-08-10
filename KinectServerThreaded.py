import socket
import sys
import threading

# Create a list to hold all client objects
ProviderList = []
ReceiverList = []

# Create a TCP/IP socket
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# Bind the socket to the port
server_address = ('0.0.0.0', 1935)
print(sys.stderr, 'starting up on %s port %s' % server_address)
sock.bind(server_address)

class Receiver(object):
    def __init__(self, name, address, subscription):
        self.name = name
        self.address = address
        self.subscription = subscription

class Provider(object):
    def __init__(self, name, address, configuration):
        self.name = name
        self.address = address
        self.configuration = configuration

def process_message(data, address):
    #print(sys.stderr, 'received %s from %s' % (data, address))
    #print(sys.stderr, data)
    split = data.decode().split("|")
    if (split[0] == "JOIN"):
        # Add the new client to the client dictionary (Should replace none with something else by default...)
        if (split[1] == "PROVIDER"):
            ProviderList.append(Provider(split[2], address, split[3]))
            print("Provider %s joined the server" % (split[2]))
        elif(split[1] == "RECEIVER"):
            target = Provider('none', '0.0.0.0', 'none')
            ReceiverList.append(Receiver(split[2], address, target))
            print("Receiver %s joined the server" % (split[2]))

        # Confirm to the new client that they're joined
        message = "CONFIRM|JOIN|" + split[1] + "|" + split[2]+ "|"
        sock.sendto(message.encode(), address)

        # Notify all clients of the new user
        message = "NOTICE|JOIN|" + split[1] + "|" + split[2]+ "|"
        for receiver in ReceiverList:
            sock.sendto(message.encode(), receiver.address)

    elif (split[0] == "LEAVE"):
        # Remove the client from the client dictionary
        if(split[1] == "PROVIDER"):
            for provider in ProviderList:
                if(provider.name == split[2] and provider.address == address):
                    ProviderList.remove(provider)
                    print("Provider %s left the server" % (split[2]))
        elif(split[1] == "RECEIVER"):
            for receiver in ReceiverList:
                if(receiver.name == split[2] and receiver.address == address):
                    ReceiverList.remove(receiver)
                    print("Receiver %s left the server" % (split[2]))
    
        # Confirm to the client that they're removed
        message = "CONFIRM|LEAVE|" + split[1] + "|" + split[2]+ "|"
        sock.sendto(message.encode(), address)

        # Notify everyone about the removed client
        message = "NOTICE|LEAVE|" + split[1] + "|" + split[2]+ "|"
        for provider in ProviderList:
            sock.sendto(message.encode(), provider.address)
        for receiver in ReceiverList:
            sock.sendto(message.encode(), receiver.address)

    elif (split[0] == "SUBSCRIBE"):
        target = Provider('none', '0.0.0.0', 'none')
        # Find the target provider in the provider dictionary
        for provider in ProviderList:
            if(provider.name == split[1]):
                target = provider

        # Find the receiver in the receiver dictionary and set their subscription target
        for receiver in ReceiverList:
            if(receiver.name == split[2] and receiver.address == address):
                receiver.subscription = target
                print("Receiver %s has subscribed to the provider %s" % (receiver.name, receiver.subscription.name))
            
        # Confirm to the receiver that they're subscribed       
        message = "CONFIRM|SUBSCRIBE|" + split[1] + "|" + split[2]+ "|" + target.configuration
        sock.sendto(message.encode(), address)

    elif (split[0] == "UNSUBSCRIBE"):
        target = Provider('none', '0.0.0.0', 'none')

        # Find the receiver in the receiver dictionary and set their subscription target
        for receiver in ReceiverList:
            if(receiver.name == split[2] and receiver.address == address):
                receiver.subscription = target
                print("Receiver %s has unsubscribed from its provider" % (split[2]))
            
        # Confirm to the receiver that they're subscribed       
        message = "CONFIRM|UNSUBSCRIBE|" + split[2] + "|"
        sock.sendto(message.encode(), address)

    elif (split[0] == "PROVIDE"):
        # Verify that the source provider and address is registered
        for provider in ProviderList:
            if(provider.name == split[1] and provider.address == address):     
                # Confirm to the provider that the frame was recieved
                message = "CONFIRM|PROVIDE|" + split[1] + "|" + split[2]+ "|" + split[4]+ "|"
                sock.sendto(message.encode(), address)
                
                # Forward the frame to all recipients subscribed to the provider
                message = "NOTICE|PROVIDE|" + split[1] + "|" + split[2]+ "|" + split[3]+ "|" + split[4]+ "|" + split[5]+ "|"
                for receiver in ReceiverList:
                    if(receiver.subscription.name == provider.name):
                        threading.Thread(target = sock.sendto, args=(message.encode(), receiver.address,)).start()


def listen_for_messages():
    while True:
        data, address = sock.recvfrom(50000)
        threading.Thread(target = process_message, args=(data,address,)).start()
           

mainloop = threading.Thread(target=listen_for_messages, args=())

mainloop.start()
mainloop.join()


