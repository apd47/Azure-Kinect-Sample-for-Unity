import socket
import sys
import threading
import time 
from enum import Enum

class Provider(object):
    def __init__(self, name, address):
        self.name = name
        self.address = address
        self.frames = []
        self.subscribers = []
    
    def ProcessMessage(self, taskName, taskVariables):
        if(taskName == "LEAVE"):
            # LEAVE Variables:
                # None - taskVariables should be empty

            ProviderList.remove(self)
            print("Provider %s left the server" % (self.name))

            # Unsubscribe any subscribers from this provider
            for receiver in self.subscribers:
                print("Reciever %s was unsubscribed due to its provider leaving the server" % (receiver.name))

            # Confirm to the provider that they've left
            message = "SERVER|" + server_name + "|CONFIRM|" + "LEAVE," + clientType + "," + clientName + "|"
            sock.sendto(message.encode(), address)

            # Notify all clients that the provider has left
            message = "SERVER|" + server_name + "|NOTICE|" + "LEAVE," + clientType + "," + clientName + "|"
            for provider in ProviderList:
                sock.sendto(message.encode(), provider.address)
            for receiver in ReceiverList:
                sock.sendto(message.encode(), receiver.address)

        elif(taskName == "ADDFRAME"):
            # ADDFRAME Variables:
                # [0] = Frame Number
                # [1] = Frame Type (COLOR or DEPTH)
                # [2] = Number of Blocks
                # [3] = Kinect Configuration
            frameNumber = taskVariables[0]
            frameType = taskVariables[1]
            numberOfBlocks = taskVariables[2]
            kinectConfiguration = taskVariables[3]

            # Add a new Frame instance to the provider's frame list
            self.frames.append(Frame(frameNumber, frameType, numberOfBlocks, kinectConfiguration))

            # Confirm to the provider that the frame was added
            message = "SERVER|" + server_name + "|CONFIRM|" + "ADDFRAME," + frameNumber + "," + frameType + "|"
            sock.sendto(message.encode(), address)

        elif(taskName == "FINISHFRAME"):
            # FINISHFRAME Variables:
                # [0] = Frame Number
                # [1] = Frame Type (COLOR or DEPTH)
            frameNumber = taskVariables[0]
            frameType = taskVariables[1]

            # Check if the frame has received all of its blocks
            frame  = next((x for x in self.frames if x.frameNumber == frameNumber and x.frameType == frameType), None)
            if frame is None:
                message = "SERVER|" + server_name + "|FAIL|" + "FINISHFRAME," + frameNumber + "," + frameType  + ", MISSINGFRAME|"
                sock.sendto(message.encode(), address)
            else:
                if frame.blocks.contains(None):
                    message = "SERVER|" + server_name + "|FAIL|" + "FINISHFRAME," + frameNumber  + "," + frameType + ", MISSINGBLOCKS|"
                    sock.sendto(message.encode(), address)
                    for index, block in enumerate(frame.blocks):
                        if(block is None):
                            message = "SERVER|" + server_name + "|REQUESTBLOCK|" + frameNumber + "," + frameType  + "," + index + "|"
                else:
                    frame.isComplete = True
                    print("Frame %s (%s) was received fully " % (frame.frameNumber, frame.frameType))

                    # Confirm to the provider that the frame was fully recieved and can be disposed
                    message = "SERVER|" + server_name + "|CONFIRM|" + "FINISHFRAME," + frameNumber + "," + frameType + "|"
                    sock.sendto(message.encode(), address)

                    # Tell all subscribed receivers that a new complete frame is available
                    message = "SERVER|" + server_name + "|ADDFRAME|" + frameNumber + "," + frameType + "," + frame.numberOfBlocks +  "," + frame.kinectConfiguration + "|"
                    for receiver in self.subscribers:
                        sock.sendto(message.encode(), receiver.address)

        elif(taskName == "ADDBLOCK"):
            # ADDBLOCK Variables:
                # [0] = Frame Number
                # [1] = Frame Type (COLOR or DEPTH)
                # [2] = Block Number
                # [3] = Block Data
            frameNumber = taskVariables[0]
            frameType = taskVariables[1]
            blockNumber = taskVariables[2]
            blockData = taskVariables[3]

            # Add the block data to the appropriate frame
            frame  = next((x for x in self.frames if x.frameNumber == frameNumber and x.frameType == frameType), None)
            if frame is None:
                message = "SERVER|" + server_name + "|FAIL|" + "ADDBLOCK," + frameNumber  + "," + frameType + ", MISSINGFRAME|"
                sock.sendto(message.encode(), address)
            else:
                frame.blocks[blockNumber] = blockData
                # Confirm to the provider that the block was recieved
                message = "SERVER|" + server_name + "|CONFIRM|" + "ADDBLOCK," + frameNumber + "," + frameType + "," + blockNumber +"|"
                sock.sendto(message.encode(), address)

class Receiver(object): 
    def __init__(self, name, address):
        self.name = name
        self.address = address
        self.subscribedTo = None
    
    def ProcessMessage(self, taskName, taskVariables):
        if(taskName == "LEAVE"):
            # LEAVE Variables:
                # None - taskVariables should be empty

            ReceiverList.remove(self)
            print("Receiver %s left the server" % (split[2]))

            # Remove self from provider's subscriber list
            subscribedTo.remove(self)

             # Confirm to the receiver that they've left
            message = "SERVER|" + server_name + "|CONFIRM|" + "LEAVE," + clientType + "," + clientName + "|"
            sock.sendto(message.encode(), address)

            # Notify all clients that the receiver has left
            message = "SERVER|" + server_name + "|ALERT|" + "LEAVE," + clientType + "," + clientName + "|"
            for provider in ProviderList:
                sock.sendto(message.encode(), provider.address)
            for receiver in ReceiverList:
                sock.sendto(message.encode(), receiver.address)
                
        elif(taskName == "SUBSCRIBE"):
            # SUBSCRIBE Variables:
                # [0] = Provider Name
            providerName = taskVariables[0]

            # Find the target provider in the provider dictionary
            provider = next((x for x in ProviderList if x.name == providerName), None)

            if provider is None:
                print("Subscription failed for receiver %s: No provider with name %s found" % (self.name, providerName))
                # Tell the receiver that they failed to subscribe       
                message = "SERVER|" + server_name + "|FAIL|" + "SUBSCRIBE," + providerName + "|"
                sock.sendto(message.encode(), address)
            else:
                # Add as a subscriber to the provider
                provider.append(self)

                # And set own subscribedTo variable to serve as a parent reference if needed
                self.subscribedTo = provider
                print("Receiver %s has subscribed to the provider %s" % (self.name, provider.name))

                # Confirm to the receiver that they're subscribed       
                message = "SERVER|" + server_name + "|CONFIRM|" + "SUBSCRIBE," + providerName + "|"
                sock.sendto(message.encode(), address)

        elif(taskName == "UNSUBSCRIBE"):
                # UNSUBSCRIBE Variables:
                    # None - taskVariables should be empty

                if self.subscribedTo is None:
                    message = "SERVER|" + server_name + "|FAIL|" + "UNSUBSCRIBE" + "|"
                    sock.sendto(message.encode(), address)
                else:
                    # Remove self from provider's subscription list, then reset subscribedTo to None
                    subscribedTo.subscribers.remove(self)
                    subscribedTo = None
                    print("Receiver %s has unsubscribed from its provider" % (self.name))
            
                    # Confirm to the receiver that they're unsubscribed       
                    message = "SERVER|" + server_name + "|CONFIRM|" + "UNSUBSCRIBE" + "|"
                    sock.sendto(message.encode(), address)

        elif(taskName == "REQUESTFRAME"):
            # REQUESTFRAME Variables:
                # [0] = Frame Number
                # [1] = Frame Type (COLOR or DEPTH)
            frameNumber = taskVariables[0]
            frameType = taskVariables[1]

            # Check if the frame exists in the provider's frame list
            frame  = next((x for x in self.subscribedTo.frames if x.frameNumber == frameNumber and x.frameType == frameType), None)
            if frame is None:
                message = "SERVER|" + server_name + "|FAIL|" + "REQUESTFRAME," + frameNumber + "," + frameType +", MISSINGFRAME|"
                sock.sendto(message.encode(), address)
            else:
                if not frame.isComplete:
                    message = "SERVER|" + server_name + "|FAIL|" + "REQUESTFRAME," + frameNumber + "," + frameType +", INCOMPLETEFRAME|"
                    sock.sendto(message.encode(), address)
                else:
                    frame.sendAllBlocks(self)
                    # Confirm to the provider that the frame was requested and will be sent
                    message = "SERVER|" + server_name + "|CONFIRM|" + "REQUESTFRAME," + frameNumber + "," + frameType + "|"
                    sock.sendto(message.encode(), address)

    
        elif(taskName == "ADDBLOCK"):
            # ADDBLOCK Variables:
                # [0] = Frame Number
                # [1] = Frame Type (COLOR or DEPTH)
                # [2] = Block Number
                # [3] = Block Data
            frameNumber = taskVariables[0]
            frameType = taskVariables[1]
            blockNumber = taskVariables[2]
            blockData = taskVariables[3]

            # Add the block data to the appropriate frame
            frame  = next((x for x in self.frames if x.frameNumber == frameNumber and x.frameType == frameType), None)
            if frame is None:
                message = "SERVER|" + server_name + "|FAIL|" + "ADDBLOCK," + frameNumber + ", MISSINGFRAME|"
                sock.sendto(message.encode(), address)
            else:
                frame.blocks[blockNumber] = blockData
                # Confirm to the provider that the block was recieved
                message = "SERVER|" + server_name + "|CONFIRM|" + "ADDBLOCK," + frameNumber + "," + frameType + "," + blockNumber +"|"
                sock.sendto(message.encode(), address)

        elif(taskName == "REQUESTBLOCK"):
            # REQUESTBLOCK Variables:
                # [0] = Frame Number
                # [1] = Frame Type (COLOR or DEPTH)
                # [2] = Block Number
            frameNumber = taskVariables[0]
            frameType = taskVariables[1]
            blockNumber = taskVariables[2]

            # Check if the frame exists in the provider's frame list
            frame  = next((x for x in self.subscribedTo.frames if x.frameNumber == frameNumber and x.frameType == frameType), None)
            if frame is None:
                message = "SERVER|" + server_name + "|FAIL|" + "REQUESTBLOCK," + frameNumber + "," + frameType + "," + blockNumber + ", MISSINGFRAME|"
                sock.sendto(message.encode(), address)
            else:
                if frame.blocks[blockNumber] is None:
                    message = "SERVER|" + server_name + "|FAIL|" + "REQUESTBLOCK," + frameNumber + "," + frameType + "," + blockNumber + ", EMPTYBLOCK|"
                    sock.sendto(message.encode(), address)
                else:
                    frame.sendBlock(self, blockNumber)
                    # Confirm to the provider that the block was requested and will be sent
                    message = "SERVER|" + server_name + "|CONFIRM|" + "REQUESTBLOCK," + frameNumber + "," + frameType + "," + blockNumber + "|"
                    sock.sendto(message.encode(), address)

class Frame(object):
    def __init__(self, frameNumber, frameType, numberOfBlocks, configuration):
        self.frameNumber = frameNumber
        self.frameType  = frameType
        self.numberOfBlocks = numberOfBlocks
        self.blocks = [None] * numberOfBlocks
        self.configuration = configuration
        self.isComplete = False        

    def sendAllBlocks(self, receiver):
        for index, block in enumerate(self.blocks):
            message = "SERVER|" + server_name + "|ADDBLOCK|" + frameNumber + "," + frameType + "," + index + "," + block + "|"
            sock.sendto(message.encode(), receiver.address)

    def sendBlock(self, receiver, blockNumber):
            message = "SERVER|" + server_name + "|ADDBLOCK|" + frameNumber + "," + frameType + "," + blockNumber + "," + blocks[blockNumber] + "|"
            sock.sendto(message.encode(), receiver.address)
        
# Preprocess the message to split the data and delegate the message to the client object it should target
# If delegateOnNewThread is set to true, the delegated processing will happen on a new thread
# Also catches if the client is trying to join the server so an object can be instantiated
# and catches if an unregistered client tries to execute a request on the server
def PreprocessAndDelegate(data, address, delegateOnNewThread):
    # Message Structure:
    #   All messages are | delimited strings with the following order. Messages should always terminate with a "|"
    #   [0] = client type (PROVIDER, RECEIVER)
    #   [1] = client name (string)
    #   [2] = task (JOIN, LEAVE, SUBSCRIBE, UNSUBSCRIBE, ADDFRAME, CONFIRMFRAME, ADDBLOCK, REQUESTBLOCK)
    #   [3] = task variables (string, comma seperated values)

    message = data.decode().split("|")
    clientType = message[0]
    clientName = message[1]
    taskName = message[2]
    taskVariables = message[3].split(",")


    if taskName == "JOIN":
        # JOIN Variables:
            # None - taskVariables should be empty
        if clientType == "PROVIDER":
            ProviderList.append(Provider(clientName, address))
            print("Provider %s joined the server" % (clientName))
        elif clientType == "RECEIVER":     
            ReceiverList.append(Receiver(clientName, address))
            print("Receiver %s joined the server" % (clientName))

        # Confirm to the new client that they're joined
        message = "SERVER|" + server_name + "|CONFIRM|" + "JOIN," + clientType + "," + clientName + "|"
        sock.sendto(message.encode(), address)

        # Notify all clients of the new user
        message = "SERVER|" + server_name + "|ALERT|" + "JOIN," + clientType + "," + clientName + "|"
        for receiver in ReceiverList:
            sock.sendto(message.encode(), receiver.address)

    else:
        if clientType == "PROVIDER":
            client = next((x for x in ProviderList if x.name == clientName and x.address == address), None)
        elif clientType == "RECEIVER": 
            client = next((x for x in ReceiverList if x.name == clientName and x.address == address), None)

        if client is not None:
            if delegateOnNewThread:
                threading.Thread(target = client.ProcessMessage, args=(taskName, taskVariables,)).start()
            else:
                client.ProcessMessage(taskName,taskVariables)
        else:
            print(sys.stderr, 'Unregistered client "%s" is trying to execute a %s request as a %s' % (clientName, taskName, clientType))





## RUNTIME CODE
# Create a list to hold all client objects
ProviderList = []
ReceiverList = []

# Create a UDP Socket
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

# Bind all interfaces to the port
server_address = ('0.0.0.0', 1935)
server_name = "testing"
print(sys.stderr, 'starting up on %s port %s' % server_address)
sock.bind(server_address)

   


def listen_for_messages():
    while True:
        data, address = sock.recvfrom(50000)
        threading.Thread(target = PreprocessAndDelegate, args=(data,address,True,)).start()
           

mainloop = threading.Thread(target=listen_for_messages, args=())

mainloop.start()
mainloop.join()


